using System;
using System.Collections.Generic;
using System.Globalization;
using Orbis.V1;
using UnityEngine;

namespace Orbis.Core
{
    public static class GeoPresentation
    {
        public static Vector2 MercatorUv(GeoCoordinate point)
        {
            var latitude = Math.Clamp(point.LatitudeDegrees, -85.05112878, 85.05112878) * Math.PI / 180;
            return new Vector2((float)((point.LongitudeDegrees + 180) / 360),
                (float)(.5 + Math.Log(Math.Tan(Math.PI / 4 + latitude / 2)) / (2 * Math.PI)));
        }

        public static string VertexKey(LatLon point) => Math.Round(point.Lat, 6).ToString("F6", CultureInfo.InvariantCulture)
            + ":" + Math.Round(GeoMath.WrapLongitude(point.Lon), 6).ToString("F6", CultureInfo.InvariantCulture);

        public static Dictionary<string, List<(LatLon From, LatLon To)>> TerritoryEdges(IEnumerable<TerritoryCell> cells)
        {
            var edges = new Dictionary<string, (string Owner, LatLon From, LatLon To)>();
            foreach (var cell in cells)
                for (var index = 0; index < cell.Boundary.Count; index++)
                {
                    var from = cell.Boundary[index];
                    var to = cell.Boundary[(index + 1) % cell.Boundary.Count];
                    var fromKey = VertexKey(from);
                    var toKey = VertexKey(to);
                    var key = cell.CityId + "/" + (string.CompareOrdinal(fromKey, toKey) < 0 ? fromKey + "/" + toKey : toKey + "/" + fromKey);
                    if (!edges.Remove(key)) edges.Add(key, (cell.CityId, from, to));
                }
            var result = new Dictionary<string, List<(LatLon, LatLon)>>();
            foreach (var edge in edges.Values)
            {
                if (!result.TryGetValue(edge.Owner, out var group)) result.Add(edge.Owner, group = new());
                group.Add((edge.From, edge.To));
            }
            return result;
        }

        public static bool AboveHorizon(Vector3 position, Vector3 cameraPosition)
            => Vector3.Dot(position.normalized, cameraPosition - position) > 0;

        public static GeoCoordinate FromNormal(Vector3 normal) => GeoMath.FromEcef(new Double3(
            normal.x * GeoMath.EarthRadiusMeters, normal.y * GeoMath.EarthRadiusMeters, normal.z * GeoMath.EarthRadiusMeters));
    }
}
