using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        const int MaximumFrameBytes = 8 * 1024 * 1024;
        const float AdminPollSeconds = 1f;
        const float CityPollSeconds = 0.5f;
        const float HubPollSeconds = 5f;

        enum RequestKind
        {
            Authenticate,
            Admin,
            City,
            World,
            Mutation,
            Hub,
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

        readonly ConcurrentQueue<(int Generation, byte[] Bytes)> received = new();
        readonly ConcurrentQueue<(int Generation, SocketSignal Signal)> signals = new();
        readonly Dictionary<ulong, RequestKind> pending = new();
        readonly Dictionary<ulong, CommandReceipt> receipts = new();
        readonly Dictionary<string,string> operations = new();
        readonly Dictionary<string,ulong> refreshAfter = new();
        readonly SemaphoreSlim sendGate = new(1, 1);

        /// <summary>Economy read model (S1.3b) fed by hub/market/caravan server messages.</summary>
        public EconomyStore Economy { get; } = new EconomyStore();

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
        float nextWorldPoll;
        float nextHubPoll;
        string sessionPath;
        int socketGeneration;

        public void Initialize(ClientRuntimeConfig runtimeConfig, GameStore gameStore)
        {
            config = runtimeConfig ?? throw new ArgumentNullException(nameof(runtimeConfig));
            store = gameStore ?? throw new ArgumentNullException(nameof(gameStore));
            sessionPath = Path.Combine(Application.persistentDataPath, "session.json");
            var restored = RestoreSession();
            if (restored != null)
            {
                store.Update(store.State.With(session: restored, status: "reconnecting", connection: ConnectionState.Connecting));
                StartSocket();
            }
        }

        public void Login(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                SetStatus("credentials_required");
                return;
            }

            StartCoroutine(LoginRoutine(username.Trim(), password));
        }

        public void Logout()
        {
            StopSocket();
            operations.Clear(); refreshAfter.Clear();
            if (File.Exists(sessionPath)) File.Delete(sessionPath);
            adminCursor = 0;
            Economy.Clear();
            store.Update(store.State.With(
                status: "logged_out",
                connection: ConnectionState.Offline,
                selectedCityId: string.Empty,
                clearSession: true,
                clearAdmin: true,
                clearCity: true, clearWorld: true, clearSelection: true, tool: new CityTool()));
        }

        public void SelectCity(string cityId)
        {
            var value = cityId ?? string.Empty;
            store.Update(store.State.With(selectedCityId: value, clearCity: value != store.State.SelectedCityId, tool: new CityTool()));
            nextCityPoll = 0;
            PublishPending();
        }

        public void MoveRuler(GeoCoordinate position) => Submit(new Command
        {
            MoveRuler = new MoveRuler { Destination = new LatLon { Lat = position.LatitudeDegrees, Lon = position.LongitudeDegrees } },
        });

        public void FoundCity(string name, GeoCoordinate position)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            Submit(new Command { FoundCity = new FoundCity { Name = name.Trim(), Pos = new LatLon
                { Lat = position.LatitudeDegrees, Lon = position.LongitudeDegrees } } });
        }

        public void EditCell(uint x, uint y)
        {
            var state = store.State;
            if (!CityPresentation.CanEdit(state)) return;
            var city = state.City;
            var tool = state.Tool;
            if (tool.Kind == "building")
                Submit(new Command { QueueBuilding = new QueueBuilding { CityId = city.CityId,
                    ExpectedRevision = city.Revision, BuildingId = tool.Value, X = x, Y = y, Orientation = tool.Orientation } });
            else if (tool.Kind == "road" || tool.Kind == "zone")
            {
                var edits = new ApplyCityEdits { CityId = city.CityId, ExpectedRevision = city.Revision };
                edits.Edits.Add(new CityCellEdit { X = x, Y = y, Layer = tool.Kind, Value = tool.Value });
                Submit(new Command { ApplyCityEdits = edits });
            }
        }

        public void CancelConstruction(string orderId)
        {
            if (!CityPresentation.CanEdit(store.State)) return;
            Submit(new Command { CancelConstruction = new CancelConstruction { CityId = store.State.City.CityId,
                ExpectedRevision = store.State.City.Revision, OrderId = orderId } });
        }

        /// <summary>Opens (or closes with an empty id) a trading post and asks for its fresh detail.</summary>
        public void SelectHub(string hubId)
        {
            var value = hubId ?? string.Empty;
            if (value == Economy.SelectedHubId) return;
            Economy.SelectHub(value);
            if (value.Length == 0) return;
            SubmitRead(new Command { GetHub = new GetHub { HubId = value } });
        }

        /// <summary>Reads the order book and local price for a commodity at a hub.</summary>
        public void RequestMarket(string hubId, string commodity)
        {
            if (string.IsNullOrEmpty(hubId) || string.IsNullOrEmpty(commodity)) return;
            SubmitRead(new Command { GetMarket = new GetMarket { HubId = hubId, Commodity = commodity } });
        }

        /// <summary>Dispatches a caravan from the ruler's own city; the server owns the outcome.</summary>
        public void DispatchCaravan(string cityId, string hubId, string commodity, uint quantity, ulong metal)
        {
            var ruler = store.State.World?.Ruler;
            if (string.IsNullOrEmpty(cityId) || string.IsNullOrEmpty(hubId) || string.IsNullOrEmpty(commodity)
                || ruler?.CityId != cityId) return;
            var caravan = new CreateCaravan { CityId = cityId, HubId = hubId, ExpectedRevision = ruler.Revision, Metal = metal };
            caravan.Cargo.Add(commodity, quantity);
            Submit(new Command { CreateCaravan = caravan });
        }

        /// <summary>Read-only economy request: never enters the mutation lane or flips the pending HUD.</summary>
        void SubmitRead(Command command)
        {
            if (!authenticated || socket?.State != WebSocketState.Open) return;
            command.RequestId = Guid.NewGuid().ToString("N");
            Send(command, RequestKind.Hub);
        }

        void Submit(Command command)
        {
            var cityId = CommandCity(command);
            if (!authenticated || socket?.State!=WebSocketState.Open || store.State.Connection != ConnectionState.Online || operations.ContainsKey(cityId) || refreshAfter.ContainsKey(cityId)) return;
            command.RequestId = Guid.NewGuid().ToString("N");
            operations[cityId] = command.RequestId;
            PublishPending();
            Send(command, RequestKind.Mutation);
        }

        static string CommandCity(Command command) => command.QueueBuilding?.CityId ?? command.ApplyCityEdits?.CityId
            ?? command.CancelConstruction?.CityId ?? command.InspectCity?.CityId ?? command.CreateCaravan?.CityId ?? "";

        void PublishPending()
        {
            var cityId = store.State.SelectedCityId;
            var value = operations.TryGetValue(cityId, out var operation) ? operation
                : operations.TryGetValue("", out var worldOperation) ? worldOperation
                : refreshAfter.ContainsKey(cityId) || refreshAfter.ContainsKey("") ? "refresh_required" : "";
            if (store.State.PendingOperation != value) store.Update(store.State.With(pendingOperation: value));
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
            SetStatus("connecting", ConnectionState.Connecting);
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
                SetStatus(response.error == "invalid_credentials" ? "invalid_credentials" : "server_unavailable", ConnectionState.Offline);
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
            StopSocket(); operations.Clear(); refreshAfter.Clear();
            Economy.Clear();
            store.Update(store.State.With(session: session, status: "connecting", connection: ConnectionState.Connecting,
                clearWorld: true, clearCity: true, clearAdmin: true, clearSelection: true, clearPreview: true,
                selectedCityId: "", tool: new CityTool()));
            StartSocket();
        }

        void Update()
        {
            if(store==null)return;
            while (signals.TryDequeue(out var signal))
            {
                if (signal.Generation != socketGeneration) continue;
                if (signal.Signal == SocketSignal.Connected)
                {
                    authenticated = false;
                    initialAdminWorld = true;
                    pending.Clear();
                    receipts.Clear();
                    foreach(var cityId in operations.Keys)refreshAfter[cityId]=0;
                    operations.Clear();
                    SetStatus("authenticating", ConnectionState.Authenticating);
                    Send(new Command
                    {
                        RequestId = Guid.NewGuid().ToString("N"),
                        Authenticate = new Authenticate { Token = store.State.Session?.Token ?? string.Empty },
                    }, RequestKind.Authenticate);
                }
                else
                {
                    authenticated = false;
                    pending.Clear();
                    receipts.Clear();
                    foreach (var cityId in operations.Keys) refreshAfter[cityId] = 0;
                    operations.Clear();
                    store.Update(store.State.With(status: "reconnecting", connection: ConnectionState.Connecting, pendingOperation: ""));
                }
            }

            while (received.TryDequeue(out var message))
                if (message.Generation == socketGeneration) HandleEnvelope(message.Bytes);
            PublishPending();
            if (!authenticated || store.State.Session == null) return;

            if (receipts.Values.Any(receipt => Time.unscaledTime - receipt.SentAt > 15f))
            {
                StartSocket();
                store.Update(store.State.With(connection: ConnectionState.Connecting, pendingOperation: "", status: "reconnecting"));
                return;
            }
            if (!store.State.Session.IsAdmin && Time.unscaledTime >= nextWorldPoll && !pending.ContainsValue(RequestKind.World))
            {
                nextWorldPoll = Time.unscaledTime + 1f;
                Send(new Command { RequestId = Guid.NewGuid().ToString("N"), Viewport = new SetViewport
                {
                    Center = new LatLon { Lat = store.State.Focus.LatitudeDegrees, Lon = store.State.Focus.LongitudeDegrees },
                    Zoom = 4, WidthPx = Screen.width, HeightPx = Screen.height,
                } }, RequestKind.World);
            }

            if (Time.unscaledTime >= nextHubPoll && !pending.ContainsValue(RequestKind.Hub))
            {
                nextHubPoll = Time.unscaledTime + HubPollSeconds;
                Send(new Command { RequestId = Guid.NewGuid().ToString("N"), ListHubs = new ListHubs() }, RequestKind.Hub);
            }

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
                SetStatus("invalid_response");
                return;
            }

            if (!pending.TryGetValue(envelope.Seq, out var requestKind)) return;
            if (!receipts.TryGetValue(envelope.Seq, out var receipt) || !receipt.Accept(envelope, socketGeneration)) return;
            pending.Remove(envelope.Seq);
            receipts.Remove(envelope.Seq);
            var server = envelope.Server;
            if (server == null) return;

            if (requestKind == RequestKind.Mutation)
            {
                operations.Remove(receipt.CityId);
                refreshAfter[receipt.CityId] = envelope.Seq;
                nextCityPoll = 0;
                nextWorldPoll = 0;
            }
            if (server.World != null)
            {
                store.Update(store.State.With(world: server.World));
                if (refreshAfter.TryGetValue("", out var afterWorld) && envelope.Seq > afterWorld) refreshAfter.Remove("");
                if (string.IsNullOrEmpty(store.State.SelectedCityId) && !string.IsNullOrEmpty(server.World.Ruler?.CityId))
                    SelectCity(server.World.Ruler.CityId);
            }

            if (server.Result != null)
            {
                if(!server.Result.Ok&&server.Result.Error=="authentication_required")
                {
                    StopSocket();operations.Clear();refreshAfter.Clear();
                    if(File.Exists(sessionPath))File.Delete(sessionPath);
                    Economy.Clear();
                    store.Update(store.State.With(clearSession:true,clearCity:true,clearWorld:true,clearAdmin:true,selectedCityId:"",clearPreview:true,
                        connection:ConnectionState.Offline,status:"authentication_required",tool:new CityTool()));
                    return;
                }
                if (requestKind == RequestKind.Authenticate && server.Result.Ok)
                {
                    authenticated = true;
                    nextWorldPoll = 0;
                    nextCityPoll = 0;
                    nextHubPoll = 0;
                    Economy.Clear();
                    SetStatus(store.State.Session.IsAdmin ? "admin_live" : "online", ConnectionState.Online);
                }
                else if (!server.Result.Ok && server.Result.Error == "admin_required")
                {
                    var session = store.State.Session;
                    session.Role = "player";
                    File.WriteAllText(sessionPath, JsonUtility.ToJson(session));
                    store.Update(store.State.With(session: session, status: "admin_revoked", clearAdmin: true));
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
                store.Update(store.State.With(admin: merged, status: "admin_live", connection: ConnectionState.Online));
            }

            if (server.Hubs != null) Economy.ApplyHubs(server.Hubs);
            if (server.Hub != null) Economy.ApplyHub(server.Hub);
            if (server.Market != null) Economy.ApplyMarket(server.Market);
            if (server.Caravan != null) Economy.ApplyCaravan(server.Caravan);
            if (server.City != null && server.City.CityId == store.State.SelectedCityId
                && (store.State.City == null || server.City.Revision >= store.State.City.Revision))
            {
                store.Update(store.State.With(city: server.City));
            }
            if (server.City != null && requestKind == RequestKind.City
                && refreshAfter.TryGetValue(server.City.CityId, out var afterCity) && envelope.Seq > afterCity)
                refreshAfter.Remove(server.City.CityId);
            PublishPending();
        }

        void Send(Command command, RequestKind kind)
        {
            if (socket == null || socket.State != WebSocketState.Open) return;
            var sequence = nextSequence++;
            pending[sequence] = kind;
            var revision = command.QueueBuilding?.ExpectedRevision ?? command.ApplyCityEdits?.ExpectedRevision
                ?? command.CancelConstruction?.ExpectedRevision ?? command.CreateCaravan?.ExpectedRevision ?? 0;
            receipts[sequence] = new CommandReceipt(sequence, socketGeneration, command.RequestId, CommandCity(command), revision, Time.unscaledTime);
            var bytes = new Envelope { Seq = sequence, Command = command }.ToByteArray();
            _ = SendBytesAsync(socket, bytes, lifetime.Token);
        }

        async Task SendBytesAsync(ClientWebSocket activeSocket, byte[] bytes, CancellationToken cancellationToken)
        {
            try { await sendGate.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            try
            {
                if (activeSocket?.State == WebSocketState.Open)
                {
                    await activeSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            catch (ObjectDisposedException) { }
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
            Interlocked.Increment(ref socketGeneration);
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = null;
            socket?.Dispose();
            socket = null;
            pending.Clear();
            receipts.Clear();
            foreach (var cityId in operations.Keys) refreshAfter[cityId] = 0;
            operations.Clear();
            if (store != null) store.Update(store.State.With(pendingOperation: ""));
        }

        async Task SocketLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var generation = Interlocked.Increment(ref socketGeneration);
                try
                {
                    socket?.Dispose();
                    socket = new ClientWebSocket();
                    socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                    await socket.ConnectAsync(config.WebSocketEndpoint, cancellationToken).ConfigureAwait(false);
                    signals.Enqueue((generation, SocketSignal.Connected));
                    await ReceiveLoopAsync(socket, generation, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (WebSocketException) { }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                finally
                {
                    signals.Enqueue((generation, SocketSignal.Disconnected));
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
            }
        }

        async Task ReceiveLoopAsync(ClientWebSocket activeSocket, int generation, CancellationToken cancellationToken)
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

                if (result.MessageType == WebSocketMessageType.Binary) received.Enqueue((generation, stream.ToArray()));
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
        }
    }
}
