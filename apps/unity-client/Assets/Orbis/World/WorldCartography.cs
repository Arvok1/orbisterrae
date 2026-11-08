using System;
using System.Collections.Generic;
using System.Linq;
using Orbis.Core;
using Orbis.V1;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.World
{
    public sealed class WorldCartography : MonoBehaviour
    {
        sealed class CityVisual
        {
            public CityState State;
            public GameObject Marker;
            public Renderer Renderer;
            public Mesh Boundary;
            public GameObject Border;
            public string Signature;
        }
        readonly Dictionary<string, CityVisual> cities = new();
        MaterialPropertyBlock properties;
        GameStore store;
        GlobeRenderer globe;
        Camera worldCamera;
        Material material;
        Material borderMaterial;
        object source;
        int level = -1;
        int sampleLod;
        bool dirty = true;
        public int CityVisualCount => cities.Count;

        public void Initialize(GameStore gameStore, GlobeRenderer terrain)
        {
            store = gameStore;
            properties=new MaterialPropertyBlock();
            globe = terrain;
            worldCamera = Camera.main;
            material = new Material(Resources.Load<Material>("Orbis/WorldRelief")) { enableInstancing = true };
            borderMaterial=new Material(Resources.Load<Shader>("Orbis/CartographicInk"));
            borderMaterial.SetColor("_BaseColor",new Color(.65f,.49f,.25f));
        }

        public string Pick(Vector2 pointer)
        {
            if (worldCamera == null) return null;
            string nearest = null;
            var best = 22f * 22f;
            foreach (var pair in cities)
            {
                if (!pair.Value.Marker.activeSelf) continue;
                var screen = worldCamera.WorldToScreenPoint(pair.Value.Marker.transform.position);
                var distance = ((Vector2)screen - pointer).sqrMagnitude;
                if (screen.z > 0 && distance < best) { nearest = pair.Key; best = distance; }
            }
            return nearest;
        }

        void LateUpdate()
        {
            if (store == null || worldCamera == null) return;
            var state = store.State;
            object next = state.Session?.IsAdmin == true ? state.Admin : state.World;
            if (!ReferenceEquals(source, next)) dirty = true;
            var nextLevel = state.AltitudeMeters > 2000000 ? 2 : state.AltitudeMeters > 100000 ? 1 : 0;
            if (nextLevel != level) { level = nextLevel; sampleLod = SampleLod(level); dirty = true; }
            if (dirty)
            {
                dirty = false;
                IEnumerable<CityState> cityStates = state.Session?.IsAdmin == true ? state.Admin?.Cities : state.World?.Cities;
                IEnumerable<TerritoryCell> cells = state.Session?.IsAdmin == true ? state.Admin?.Territory : state.World?.Territory;
                Sync(cityStates ?? Array.Empty<CityState>(), cells ?? Array.Empty<TerritoryCell>());
            }
            foreach (var pair in cities)
            {
                var visual = pair.Value;
                var position = visual.Marker.transform.position;
                var visible = GeoPresentation.AboveHorizon(position, worldCamera.transform.position);
                var screen = worldCamera.WorldToViewportPoint(position);
                visible &= screen.z > 0 && screen.x > -.1 && screen.x < 1.1 && screen.y > -.1 && screen.y < 1.1;
                visual.Marker.SetActive(visible && !(pair.Key == state.SelectedCityId && state.AltitudeMeters < 20000));
                if (visual.Border != null) visual.Border.SetActive(visible);
                var scale = Vector3.Distance(position, worldCamera.transform.position) * 24f
                    * Mathf.Tan(worldCamera.fieldOfView * Mathf.Deg2Rad / 2) / Mathf.Max(1, Screen.height);
                visual.Marker.transform.localScale = Vector3.one * Mathf.Max(.008f, scale);
                var own = visual.State.KingdomId == state.World?.Ruler?.KingdomId;
                var color = pair.Key == state.SelectedCityId ? new Color(.85f,.23f,.13f)
                    : !visual.State.Active ? new Color(.8f,.54f,.2f) : own ? new Color(.44f,.66f,.35f) : new Color(.65f,.58f,.4f);
                properties.SetColor("_BaseColor", color);
                visual.Renderer.SetPropertyBlock(properties);
            }
        }

        void Sync(IEnumerable<CityState> cityStates, IEnumerable<TerritoryCell> cells)
        {
            var retained = new HashSet<string>();
            var edges = GeoPresentation.TerritoryEdges(cells);
            foreach (var city in cityStates)
            {
                if (city.Pos == null) continue;
                retained.Add(city.Id);
                if (!cities.TryGetValue(city.Id, out var visual))
                {
                    var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    marker.transform.SetParent(transform, false);
                    Destroy(marker.GetComponent<Collider>());
                    visual = new CityVisual { Marker = marker, Renderer = marker.GetComponent<Renderer>() };
                    visual.Renderer.sharedMaterial = material;
                    cities.Add(city.Id, visual);
                }
                visual.State = city;
                visual.Marker.name = city.Name;
                visual.Marker.transform.position = Position(city.Pos, 35);
                visual.Marker.transform.rotation = Quaternion.FromToRotation(Vector3.up, visual.Marker.transform.position.normalized);
                var group = edges.TryGetValue(city.Id, out var segments) ? segments : new List<(LatLon From, LatLon To)>();
                var signature = string.Join(";", group.Select(edge => GeoPresentation.VertexKey(edge.From) + "/" + GeoPresentation.VertexKey(edge.To)));
                if (signature == visual.Signature) continue;
                visual.Signature = signature;
                if (visual.Boundary != null) Destroy(visual.Boundary);
                var vertices = new List<Vector3>();
                foreach (var edge in group) { vertices.Add(Position(edge.From, 20)); vertices.Add(Position(edge.To, 20)); }
                visual.Boundary = GeographicOverlay.LineMesh(vertices, "Territory " + city.Id);
                if (visual.Border == null)
                {
                    visual.Border = new GameObject("Territory " + city.Id);
                    visual.Border.transform.SetParent(transform, false);
                    visual.Border.AddComponent<MeshFilter>();
                    visual.Border.AddComponent<MeshRenderer>().sharedMaterial = borderMaterial;
                }
                visual.Border.GetComponent<MeshFilter>().sharedMesh = visual.Boundary;
            }
            foreach (var key in cities.Keys.Where(key => !retained.Contains(key)).ToArray())
            {
                var visual = cities[key];
                Destroy(visual.Marker);
                if (visual.Border != null) Destroy(visual.Border);
                if (visual.Boundary != null) Destroy(visual.Boundary);
                cities.Remove(key);
            }
        }

        /// <summary>Relief LOD sampled for each zoom level: fine close up, coarse from orbit.</summary>
        static int SampleLod(int level) => level switch { 0 => 4, 1 => 2, _ => 0 };

        Vector3 Position(LatLon position, float lift)
        {
            var coordinate = new GeoCoordinate(position.Lat, position.Lon);
            return GeoMath.SurfaceNormal(coordinate) * (float)((GeoMath.EarthRadiusMeters + globe.SampleHeightMeters(coordinate, sampleLod) + lift) / GeoMath.MetersPerUnityUnit);
        }
        void OnDestroy()
        {
            foreach (var visual in cities.Values) if (visual.Boundary != null) Destroy(visual.Boundary);
            if (material != null) Destroy(material);
            if(borderMaterial!=null)Destroy(borderMaterial);
        }
    }
}
