using System;
using Orbis.V1;
using UnityEngine;

namespace Orbis.Core
{
    public sealed class CityTool
    {
        public string Kind { get; }
        public string Value { get; }
        public uint Orientation { get; }
        public CityTool(string kind = "select", string value = "", uint orientation = 0)
        {
            Kind = kind;
            Value = value;
            Orientation = orientation % 4;
        }
    }

    public static class CityPresentation
    {
        public const double CellMeters = 10;
        public static byte[] DecodeRle(byte[] encoded, int expected)
        {
            if (encoded == null || expected < 0 || encoded.Length % 3 != 0) throw new ArgumentException("invalid_rle");
            var result = new byte[expected];
            var cursor = 0;
            for (var offset = 0; offset < encoded.Length; offset += 3)
            {
                var count = encoded[offset + 1] | encoded[offset + 2] << 8;
                if (count == 0 || count > expected - cursor) throw new ArgumentException("invalid_rle");
                Array.Fill(result, encoded[offset], cursor, count);
                cursor += count;
            }
            if (cursor != expected) throw new ArgumentException("invalid_rle");
            return result;
        }
        public static Vector2Int Footprint(BuildingDefinition definition, uint orientation) => orientation % 2 == 0
            ? new Vector2Int((int)definition.Width, (int)definition.Height)
            : new Vector2Int((int)definition.Height, (int)definition.Width);
        public static GeoCoordinate CellCoordinate(GeoCoordinate origin, uint width, uint height, double x, double y)
        {
            var north = (height / 2d - y) * CellMeters;
            var east = (x - width / 2d) * CellMeters;
            var radians = Math.PI / 180;
            return new GeoCoordinate(origin.LatitudeDegrees + north / GeoMath.EarthRadiusMeters / radians,
                origin.LongitudeDegrees + east / (GeoMath.EarthRadiusMeters * Math.Max(.01, Math.Cos(origin.LatitudeDegrees * radians))) / radians);
        }
        public static bool TryCell(GeoCoordinate origin, uint width, uint height, GeoCoordinate point, out Vector2Int cell)
        {
            var radians = Math.PI / 180;
            var east = GeoMath.WrapLongitude(point.LongitudeDegrees - origin.LongitudeDegrees) * radians
                * GeoMath.EarthRadiusMeters * Math.Max(.01, Math.Cos(origin.LatitudeDegrees * radians));
            var north = (point.LatitudeDegrees - origin.LatitudeDegrees) * radians * GeoMath.EarthRadiusMeters;
            cell = new Vector2Int((int)Math.Floor(east / CellMeters + width / 2d), (int)Math.Floor(height / 2d - north / CellMeters));
            return cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height;
        }
        public static uint Variant(string identity)
        {
            var hash = 2166136261u;
            foreach (var character in identity ?? string.Empty) hash = unchecked((hash ^ character) * 16777619u);
            return hash % 3;
        }
        public static bool CanEdit(ClientViewState state) => state.Connection == ConnectionState.Online
            && state.City?.Editable == true && state.City.CityId == state.SelectedCityId && string.IsNullOrEmpty(state.PendingOperation);
        public static CityState FindCity(ClientViewState state, string cityId)
        {
            if (state.World != null) foreach (var city in state.World.Cities) if (city.Id == cityId) return city;
            if (state.Session?.IsAdmin == true && state.Admin != null)
                foreach (var city in state.Admin.Cities) if (city.Id == cityId) return city;
            return null;
        }
    }
}
