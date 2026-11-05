using System;

namespace Orbis.Core
{
    public sealed class ClientViewState
    {
        public string Locale { get; }
        public MapVisualStyle VisualStyle { get; }
        public GeoCoordinate Focus { get; }
        public double AltitudeMeters { get; }
        public string Status { get; }

        public ClientViewState(string locale, MapVisualStyle visualStyle, GeoCoordinate focus, double altitudeMeters, string status)
        {
            Locale = locale;
            VisualStyle = visualStyle;
            Focus = focus;
            AltitudeMeters = altitudeMeters;
            Status = status;
        }

        public ClientViewState With(
            string locale = null,
            MapVisualStyle? visualStyle = null,
            GeoCoordinate? focus = null,
            double? altitudeMeters = null,
            string status = null)
        {
            return new ClientViewState(
                locale ?? Locale,
                visualStyle ?? VisualStyle,
                focus ?? Focus,
                altitudeMeters ?? AltitudeMeters,
                status ?? Status);
        }
    }

    public sealed class GameStore
    {
        public event Action<ClientViewState> Changed;

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
    }
}
