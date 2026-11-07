using UnityEngine;

namespace Orbis.Core
{
    public readonly struct CityFrame
    {
        public readonly Matrix4x4 LocalToWorld;
        public readonly Matrix4x4 WorldToLocal;
        public readonly GeoCoordinate Origin;
        public CityFrame(GeoCoordinate origin, float elevationMeters)
        {
            Origin = origin;
            var up = GeoMath.SurfaceNormal(origin);
            var east = Vector3.Cross(Vector3.up, up);
            if (east.sqrMagnitude < .001f) east = Vector3.Cross(Vector3.forward, up);
            east.Normalize();
            var north = Vector3.Cross(up, east).normalized;
            var position = up * (float)((GeoMath.EarthRadiusMeters + elevationMeters + 1) / GeoMath.MetersPerUnityUnit);
            LocalToWorld = Matrix4x4.TRS(position, Quaternion.LookRotation(-north, up), Vector3.one * .001f);
            WorldToLocal = LocalToWorld.inverse;
        }
        public Vector3 CellCenter(uint width, uint height, float x, float y) => new(
            (x - width / 2f) * (float)CityPresentation.CellMeters, 0, (y - height / 2f) * (float)CityPresentation.CellMeters);
        public bool Pick(Ray ray, uint width, uint height, out Vector2Int cell)
        {
            var plane = new Plane(LocalToWorld.MultiplyVector(Vector3.up).normalized, LocalToWorld.MultiplyPoint3x4(Vector3.zero));
            cell = default;
            if (!plane.Raycast(ray, out var distance)) return false;
            var local = WorldToLocal.MultiplyPoint3x4(ray.GetPoint(distance));
            cell = new Vector2Int(Mathf.FloorToInt(local.x / 10 + width / 2f), Mathf.FloorToInt(local.z / 10 + height / 2f));
            return cell.x >= 0 && cell.y >= 0 && cell.x < width && cell.y < height;
        }
    }
}
