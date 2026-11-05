using System;
using UnityEngine;

namespace Orbis.Core
{
    public readonly struct Double3
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Double3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double Magnitude => Math.Sqrt(X * X + Y * Y + Z * Z);

        public Double3 Normalized
        {
            get
            {
                var magnitude = Magnitude;
                return magnitude <= double.Epsilon ? new Double3(0, 0, 0) : new Double3(X / magnitude, Y / magnitude, Z / magnitude);
            }
        }

        public Vector3 ToUnityKilometers(Double3 originMeters)
        {
            return new Vector3(
                (float)((X - originMeters.X) / GeoMath.MetersPerUnityUnit),
                (float)((Y - originMeters.Y) / GeoMath.MetersPerUnityUnit),
                (float)((Z - originMeters.Z) / GeoMath.MetersPerUnityUnit));
        }
    }

    public readonly struct GeoCoordinate
    {
        public readonly double LatitudeDegrees;
        public readonly double LongitudeDegrees;
        public readonly double AltitudeMeters;

        public GeoCoordinate(double latitudeDegrees, double longitudeDegrees, double altitudeMeters = 0)
        {
            if (latitudeDegrees < -90 || latitudeDegrees > 90) throw new ArgumentOutOfRangeException(nameof(latitudeDegrees));
            LatitudeDegrees = latitudeDegrees;
            LongitudeDegrees = GeoMath.WrapLongitude(longitudeDegrees);
            AltitudeMeters = altitudeMeters;
        }
    }

    public static class GeoMath
    {
        public const double EarthRadiusMeters = 6_371_000;
        public const double MetersPerUnityUnit = 1_000;
        public const double EarthRadiusUnity = EarthRadiusMeters / MetersPerUnityUnit;

        public static Double3 ToEcef(GeoCoordinate coordinate)
        {
            var latitude = coordinate.LatitudeDegrees * Math.PI / 180;
            var longitude = coordinate.LongitudeDegrees * Math.PI / 180;
            var radius = EarthRadiusMeters + coordinate.AltitudeMeters;
            var cosLatitude = Math.Cos(latitude);

            return new Double3(
                radius * cosLatitude * Math.Sin(longitude),
                radius * Math.Sin(latitude),
                radius * cosLatitude * Math.Cos(longitude));
        }

        public static GeoCoordinate FromEcef(Double3 ecef)
        {
            var radius = ecef.Magnitude;
            if (radius <= double.Epsilon) throw new ArgumentException("ECEF coordinate cannot be the origin.", nameof(ecef));

            return new GeoCoordinate(
                Math.Asin(ecef.Y / radius) * 180 / Math.PI,
                Math.Atan2(ecef.X, ecef.Z) * 180 / Math.PI,
                radius - EarthRadiusMeters);
        }

        public static double WrapLongitude(double longitudeDegrees)
        {
            var wrapped = (longitudeDegrees + 180) % 360;
            if (wrapped < 0) wrapped += 360;
            return wrapped - 180;
        }

        public static Vector3 SurfaceNormal(GeoCoordinate coordinate)
        {
            var normal = ToEcef(new GeoCoordinate(coordinate.LatitudeDegrees, coordinate.LongitudeDegrees)).Normalized;
            return new Vector3((float)normal.X, (float)normal.Y, (float)normal.Z);
        }
    }
}
