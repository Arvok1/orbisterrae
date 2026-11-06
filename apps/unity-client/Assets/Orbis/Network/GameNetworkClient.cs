using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Orbis.Core;
using Orbis.V1;
using UnityEngine;
using UnityEngine.Networking;

namespace Orbis.Network
{
    public sealed class GameNetworkClient : MonoBehaviour
    {
        const int MaximumFrameBytes = 64 * 1024;
        const float AdminPollSeconds = 1f;
        const float CityPollSeconds = 0.5f;

        enum RequestKind
        {
            Authenticate,
            Admin,
            City,
        }

        enum SocketSignal
        {
            Connected,
            Disconnected,
        }

        [Serializable]
        sealed class LoginRequest
        {
            public string username;
            public string password;
        }

        [Serializable]
        sealed class LoginResponse
        {
            public string token;
            public string username;
            public string accountId;
            public string role;
            public string error;
        }

        readonly ConcurrentQueue<byte[]> received = new();
        readonly ConcurrentQueue<SocketSignal> signals = new();
        readonly Dictionary<ulong, RequestKind> pending = new();
        readonly SemaphoreSlim sendGate = new(1, 1);

        ClientRuntimeConfig config;
        GameStore store;
        ClientWebSocket socket;
        CancellationTokenSource lifetime;
        ulong nextSequence = 1;
        ulong adminCursor;
        bool authenticated;
        bool initialAdminWorld = true;
        float nextAdminPoll;
        float nextCityPoll;
        string sessionPath;

        public void Initialize(ClientRuntimeConfig runtimeConfig, GameStore gameStore)
        {
            config = runtimeConfig ?? throw new ArgumentNullException(nameof(runtimeConfig));
            store = gameStore ?? throw new ArgumentNullException(nameof(gameStore));
            sessionPath = Path.Combine(Application.persistentDataPath, "session.json");
            var restored = RestoreSession();
            if (restored != null)
            {
                store.Update(store.State.With(session: restored, status: "Reconectando…", connection: ConnectionState.Connecting));
                StartSocket();
            }
        }

        public void Login(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                SetStatus("Informe usuário e senha.");
                return;
            }

            StartCoroutine(LoginRoutine(username.Trim(), password));
        }

        public void Logout()
        {
            StopSocket();
            if (File.Exists(sessionPath)) File.Delete(sessionPath);
            adminCursor = 0;
            store.Update(store.State.With(
                status: "Sessão encerrada",
                connection: ConnectionState.Offline,
                selectedCityId: string.Empty,
                clearSession: true,
                clearAdmin: true,
                clearCity: true));
        }

        public void SelectCity(string cityId)
        {
            var value = cityId ?? string.Empty;
            store.Update(store.State.With(selectedCityId: value, clearCity: value.Length == 0));
            nextCityPoll = 0;
        }

        public void SetFilters(AdminFilters filters)
        {
            adminCursor = 0;
            initialAdminWorld = true;
            store.Update(store.State.With(filters: filters ?? new AdminFilters(), clearAdmin: true));
            nextAdminPoll = 0;
        }

        IEnumerator LoginRoutine(string username, string password)
        {
            SetStatus("Entrando…", ConnectionState.Connecting);
            var payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(new LoginRequest
            {
                username = username,
                password = password,
            }));
            using var request = new UnityWebRequest(new Uri(config.HttpEndpoint, "/api/login"), UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(payload),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 10,
            };
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();

            var response = string.IsNullOrWhiteSpace(request.downloadHandler.text)
                ? new LoginResponse()
                : JsonUtility.FromJson<LoginResponse>(request.downloadHandler.text);
            if (request.result != UnityWebRequest.Result.Success || string.IsNullOrWhiteSpace(response.token))
            {
                SetStatus(response.error == "invalid_credentials" ? "Credenciais inválidas" : "Gateway indisponível", ConnectionState.Offline);
                yield break;
            }

            var session = new SessionInfo
            {
                Token = response.token,
                Username = response.username,
                AccountId = response.accountId,
                Role = response.role,
            };
            File.WriteAllText(sessionPath, JsonUtility.ToJson(session));
            store.Update(store.State.With(session: session, status: "Conectando ao mundo…", connection: ConnectionState.Connecting));
            StartSocket();
        }

        void Update()
        {
            while (signals.TryDequeue(out var signal))
            {
                if (signal == SocketSignal.Connected)
                {
                    authenticated = false;
                    initialAdminWorld = true;
                    pending.Clear();
                    SetStatus("Autenticando…", ConnectionState.Authenticating);
                    Send(new Command
                    {
                        RequestId = Guid.NewGuid().ToString("N"),
                        Authenticate = new Authenticate { Token = store.State.Session?.Token ?? string.Empty },
                    }, RequestKind.Authenticate);
                }
                else
                {
                    authenticated = false;
                    SetStatus("Reconectando…", ConnectionState.Connecting);
                }
            }

            while (received.TryDequeue(out var bytes)) HandleEnvelope(bytes);
            if (!authenticated || store.State.Session == null) return;

            if (store.State.Session.IsAdmin && Time.unscaledTime >= nextAdminPoll && !pending.ContainsValue(RequestKind.Admin))
            {
                nextAdminPoll = Time.unscaledTime + AdminPollSeconds;
                var filters = store.State.Filters;
                Send(new Command
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    AdminSnapshot = new AdminSnapshotRequest
                    {
                        AfterEventSeq = adminCursor,
                        AccountId = filters.AccountId,
                        KingdomId = filters.KingdomId,
                        CityId = filters.CityId,
                        Shard = filters.Shard ?? 0,
                        EventKind = filters.EventKind ?? 0,
                        FilterShard = filters.Shard.HasValue,
                        FilterEventKind = filters.EventKind.HasValue,
                        IncludeWorld = initialAdminWorld,
                    },
                }, RequestKind.Admin);
            }

            if (!string.IsNullOrEmpty(store.State.SelectedCityId)
                && Time.unscaledTime >= nextCityPoll
                && !pending.ContainsValue(RequestKind.City))
            {
                nextCityPoll = Time.unscaledTime + CityPollSeconds;
                Send(new Command
                {
                    RequestId = Guid.NewGuid().ToString("N"),
                    InspectCity = new InspectCity { CityId = store.State.SelectedCityId },
                }, RequestKind.City);
            }
        }

        void HandleEnvelope(byte[] bytes)
        {
            Envelope envelope;
            try
            {
                envelope = Envelope.Parser.ParseFrom(bytes);
            }
            catch (InvalidProtocolBufferException)
            {
                SetStatus("Resposta inválida do servidor");
                return;
            }

            pending.TryGetValue(envelope.Seq, out var requestKind);
            pending.Remove(envelope.Seq);
            var server = envelope.Server;
            if (server == null) return;

            if (server.Result != null)
            {
                if (requestKind == RequestKind.Authenticate && server.Result.Ok)
                {
                    authenticated = true;
                    SetStatus(store.State.Session.IsAdmin ? "ADMIN · AO VIVO" : "Online", ConnectionState.Online);
                }
                else if (!server.Result.Ok && server.Result.Error == "admin_required")
                {
                    var session = store.State.Session;
                    session.Role = "player";
                    File.WriteAllText(sessionPath, JsonUtility.ToJson(session));
                    store.Update(store.State.With(session: session, status: "Acesso administrativo revogado", clearAdmin: true));
                }
                else if (!server.Result.Ok)
                {
                    SetStatus(server.Result.Error);
                }
            }

            if (server.Admin != null)
            {
                var merged = AdminSnapshotMerge.Apply(store.State.Admin, server.Admin);
                adminCursor = server.Admin.NextEventSeq;
                initialAdminWorld = false;
                store.Update(store.State.With(admin: merged, status: "ADMIN · AO VIVO", connection: ConnectionState.Online));
            }

            if (server.City != null)
            {
                store.Update(store.State.With(city: server.City));
            }
        }

        void Send(Command command, RequestKind kind)
        {
            if (socket == null || socket.State != WebSocketState.Open) return;
            var sequence = nextSequence++;
            pending[sequence] = kind;
            var bytes = new Envelope { Seq = sequence, Command = command }.ToByteArray();
            _ = SendBytesAsync(bytes, lifetime.Token);
        }

        async Task SendBytesAsync(byte[] bytes, CancellationToken cancellationToken)
        {
            await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (socket?.State == WebSocketState.Open)
                {
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            finally
            {
                sendGate.Release();
            }
        }

        void StartSocket()
        {
            StopSocket();
            lifetime = new CancellationTokenSource();
            _ = SocketLoopAsync(lifetime.Token);
        }

        void StopSocket()
        {
            authenticated = false;
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
            socket?.Dispose();
            socket = null;
            pending.Clear();
        }

        async Task SocketLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    socket?.Dispose();
                    socket = new ClientWebSocket();
                    socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                    await socket.ConnectAsync(config.WebSocketEndpoint, cancellationToken).ConfigureAwait(false);
                    signals.Enqueue(SocketSignal.Connected);
                    await ReceiveLoopAsync(socket, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (WebSocketException) { }
                catch (IOException) { }
                finally
                {
                    signals.Enqueue(SocketSignal.Disconnected);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
            }
        }

        async Task ReceiveLoopAsync(ClientWebSocket activeSocket, CancellationToken cancellationToken)
        {
            var buffer = new byte[MaximumFrameBytes];
            while (activeSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await activeSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    if (stream.Length + result.Count > MaximumFrameBytes) return;
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Binary) received.Enqueue(stream.ToArray());
            }
        }

        SessionInfo RestoreSession()
        {
            try
            {
                if (!File.Exists(sessionPath)) return null;
                var session = JsonUtility.FromJson<SessionInfo>(File.ReadAllText(sessionPath));
                return string.IsNullOrWhiteSpace(session?.Token) ? null : session;
            }
            catch (Exception)
            {
                return null;
            }
        }

        void SetStatus(string message, ConnectionState? connection = null)
        {
            store.Update(store.State.With(status: message, connection: connection));
        }

        void OnDestroy()
        {
            StopSocket();
            sendGate.Dispose();
        }
    }
}
