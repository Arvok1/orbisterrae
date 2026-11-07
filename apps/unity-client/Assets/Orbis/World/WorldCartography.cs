using System.Collections.Generic;
using Orbis.Core;
using Orbis.V1;
using UnityEngine;

namespace Orbis.World
{
    public sealed class WorldCartography : MonoBehaviour
    {
        GameStore store;
        GlobeRenderer globe;
        object rendered;
        GameObject layer;
        Material material;
        public void Initialize(GameStore gameStore, GlobeRenderer terrain)
        {
            store = gameStore;
            globe = terrain;
            material = new Material(Resources.Load<Material>("Orbis/WorldRelief"));
            material.SetColor("_BaseColor", new Color(.72f,.53f,.23f));
        }
        void LateUpdate()
        {
            var state = store.State;
            object source = state.Session?.IsAdmin == true ? state.Admin : state.World;
            if (ReferenceEquals(source, rendered)) return;
            rendered = source;
            if (layer != null) Destroy(layer);
            layer = new GameObject("Authoritative cartography");
            layer.transform.SetParent(transform, false);
            IEnumerable<CityState> cities = state.Session?.IsAdmin == true ? state.Admin?.Cities : state.World?.Cities;
            IEnumerable<TerritoryCell> territory = state.Session?.IsAdmin == true ? state.Admin?.Territory : state.World?.Territory;
            if (territory != null) foreach (var cell in territory)
            {
                var node = new GameObject("Territory " + cell.CityId);
                node.transform.SetParent(layer.transform, false);
                var line = node.AddComponent<LineRenderer>();
                line.sharedMaterial = material;
                line.widthMultiplier = .025f;
                line.loop = true;
                line.positionCount = cell.Boundary.Count;
                for (var index = 0; index < cell.Boundary.Count; index++) line.SetPosition(index, Position(cell.Boundary[index], 15));
            }
            if (cities != null) foreach (var city in cities)
            {
                if (city.Pos == null) continue;
                var node = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                node.name = city.Name;
                node.transform.SetParent(layer.transform, false);
                node.transform.position = Position(city.Pos, 1000);
                node.transform.localScale = Vector3.one * 1.5f;
                node.GetComponent<Renderer>().sharedMaterial = material;
                Destroy(node.GetComponent<Collider>());
            }
        }
        Vector3 Position(LatLon coordinate, float lift)
        {
            var geo = new GeoCoordinate(coordinate.Lat, coordinate.Lon);
            return GeoMath.SurfaceNormal(geo) * (float)((GeoMath.EarthRadiusMeters + globe.SampleHeightMeters(geo) + lift) / GeoMath.MetersPerUnityUnit);
        }
        void OnDestroy() { if (material != null) Destroy(material); }
    }
}
