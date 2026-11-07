using System;
using Orbis.V1;

namespace Orbis.Core
{
    public enum ConnectionState
    {
        Offline,
        Connecting,
        Authenticating,
        Online,
    }

    [Serializable]
    public sealed class SessionInfo
    {
        public string Token = string.Empty;
        public string Username = string.Empty;
        public string AccountId = string.Empty;
        public string Role = "player";

        public bool IsAdmin => string.Equals(Role, "admin", StringComparison.Ordinal);
    }

    public sealed class AdminFilters
    {
        public string AccountId { get; }
        public string KingdomId { get; }
        public string CityId { get; }
        public ulong? Shard { get; }
        public uint? EventKind { get; }

        public AdminFilters(
            string accountId = "",
            string kingdomId = "",
            string cityId = "",
            ulong? shard = null,
            uint? eventKind = null)
        {
            AccountId = accountId ?? string.Empty;
            KingdomId = kingdomId ?? string.Empty;
            CityId = cityId ?? string.Empty;
            Shard = shard;
            EventKind = eventKind;
        }
    }

    public sealed class ClientViewState
    {
        public string Locale { get; }
        public MapVisualStyle VisualStyle { get; }
        public GeoCoordinate Focus { get; }
        public double AltitudeMeters { get; }
        public string Status { get; }
        public ConnectionState Connection { get; }
        public SessionInfo Session { get; }
        public AdminSnapshot Admin { get; }
        public CitySnapshot City { get; }
        public string SelectedCityId { get; }
        public AdminFilters Filters { get; }
        public WorldSnapshot World { get; }
        public GeoCoordinate? Selection { get; }
        public CityTool Tool { get; }
        public string PendingOperation { get; }
        public PlacementPreview Preview { get; }

        public ClientViewState(
            string locale,
            MapVisualStyle visualStyle,
            GeoCoordinate focus,
            double altitudeMeters,
            string status,
            ConnectionState connection = ConnectionState.Offline,
            SessionInfo session = null,
            AdminSnapshot admin = null,
            CitySnapshot city = null,
            string selectedCityId = "",
            AdminFilters filters = null,
            WorldSnapshot world = null,
            GeoCoordinate? selection = null,
            CityTool tool = null,
            string pendingOperation = "",
            PlacementPreview preview = null)
        {
            Locale = locale;
            VisualStyle = visualStyle;
            Focus = focus;
            AltitudeMeters = altitudeMeters;
            Status = status;
            Connection = connection;
            Session = session;
            Admin = admin;
            City = city;
            SelectedCityId = selectedCityId ?? string.Empty;
            Filters = filters ?? new AdminFilters();
            World = world;
            Selection = selection;
            Tool = tool ?? new CityTool();
            PendingOperation = pendingOperation;
            Preview = preview;
        }

        public ClientViewState With(
            string locale = null,
            MapVisualStyle? visualStyle = null,
            GeoCoordinate? focus = null,
            double? altitudeMeters = null,
            string status = null,
            ConnectionState? connection = null,
            SessionInfo session = null,
            AdminSnapshot admin = null,
            CitySnapshot city = null,
            string selectedCityId = null,
            AdminFilters filters = null,
            bool clearSession = false,
            bool clearAdmin = false,
            bool clearCity = false,
            WorldSnapshot world = null,
            GeoCoordinate? selection = null,
            CityTool tool = null,
            string pendingOperation = null,
            bool clearWorld = false,
            bool clearSelection = false,
            PlacementPreview preview = null,
            bool clearPreview = false)
        {
            return new ClientViewState(
                locale ?? Locale,
                visualStyle ?? VisualStyle,
                focus ?? Focus,
                altitudeMeters ?? AltitudeMeters,
                status ?? Status,
                connection ?? Connection,
                clearSession ? null : session ?? Session,
                clearAdmin ? null : admin ?? Admin,
                clearCity ? null : city ?? City,
                selectedCityId ?? SelectedCityId,
                filters ?? Filters,
                clearWorld ? null : world ?? World,
                clearSelection ? null : selection ?? Selection,
                tool ?? Tool,
                pendingOperation ?? PendingOperation,
                clearPreview ? null : preview ?? Preview);
        }
    }

    public sealed class GameStore
    {
        public event Action<ClientViewState> Changed;
        public event Action<GeoCoordinate, double> FocusRequested;

        public ClientViewState State { get; private set; }

        public GameStore(ClientViewState initialState)
        {
            State = initialState ?? throw new ArgumentNullException(nameof(initialState));
        }

        public void Update(ClientViewState nextState)
        {
            State = nextState ?? throw new ArgumentNullException(nameof(nextState));
            Changed?.Invoke(State);
        }

        public void Focus(GeoCoordinate coordinate, double altitudeMeters)
        {
            FocusRequested?.Invoke(coordinate, altitudeMeters);
        }
    }
}
