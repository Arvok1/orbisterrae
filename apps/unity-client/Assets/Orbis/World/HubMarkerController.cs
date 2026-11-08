using System.Collections.Generic;
using Orbis.Core;
using Orbis.V1;
using UnityEngine;

namespace Orbis.World
{
    /// <summary>
    /// Gold markers for the AI trading hubs observed through the economy store
    /// (S1.3b). Mirrors the city markers of WorldCartography with the smallest
    /// surface that honors their behavior: relief-sampled placement, horizon and
    /// viewport culling, distance-aware scale, and screen-space picking.
    /// Hidden for admin observers, who watch the world without trading.
    /// </summary>
    public sealed class HubMarkerController : MonoBehaviour
    {
        sealed class HubVisual
        {
            public string Id;
            public string Name;
            public GameObject Marker;
            public Renderer Renderer;
        }

        readonly Dictionary<string,HubVisual> hubs=new();
        readonly List<string> removed=new();
        MaterialPropertyBlock properties;
        Material material;
        GameStore store;
        EconomyStore economy;
        GlobeRenderer globe;
        Camera worldCamera;
        bool dirty = true;
        public int HubVisualCount => hubs.Count;

        public void Initialize(GameStore gameStore, EconomyStore economyStore, GlobeRenderer terrain)
        {
            store = gameStore;
            economy = economyStore;
            globe = terrain;
            worldCamera = Camera.main;
            properties = new MaterialPropertyBlock();
            material = new Material(Resources.Load<Material>("Orbis/WorldRelief")) { enableInstancing = true };
            economy.Changed += OnEconomyChanged;
        }

        void OnEconomyChanged()=>dirty = true;

        void LateUpdate()
        {
            if (store == null || worldCamera == null) return;
            if (dirty) { dirty = false; Sync(); }
            var state = store.State;
            var playerWatching = state.Session != null && !state.Session.IsAdmin;
            foreach (var pair in hubs)
            {
                var visual = pair.Value;
                var position = visual.Marker.transform.position;
                var visible = playerWatching && GeoPresentation.AboveHorizon(position, worldCamera.transform.position);
                var screen = worldCamera.WorldToViewportPoint(position);
                visible &= screen.z > 0 && screen.x > -.1 && screen.x < 1.1 && screen.y > -.1 && screen.y < 1.1;
                visual.Marker.SetActive(visible);
                if (!visible) continue;
                var scale = Vector3.Distance(position, worldCamera.transform.position) * 24f
                    * Mathf.Tan(worldCamera.fieldOfView * Mathf.Deg2Rad / 2) / Mathf.Max(1, Screen.height);
                visual.Marker.transform.localScale = Vector3.one * Mathf.Max(.008f, scale);
                properties.SetColor("_BaseColor", pair.Key == economy.SelectedHubId
                    ? new Color(.85f,.23f,.13f)
                    : new Color(.79f,.64f,.16f));
                visual.Renderer.SetPropertyBlock(properties);
            }
        }

        /// <summary>Nearest visible hub under the pointer, or null when none is close enough.</summary>
        public string Pick(Vector2 pointer)
        {
            if (worldCamera == null || hubs.Count == 0) return null;
            if (store.State.Session == null || store.State.Session.IsAdmin) return null;
            string nearest = null;
            var best = 24f * 24f;
            foreach (var pair in hubs)
            {
                if (!pair.Value.Marker.activeSelf) continue;
                var screen = worldCamera.WorldToScreenPoint(pair.Value.Marker.transform.position);
                var distance = ((Vector2)screen - pointer).sqrMagnitude;
                if (screen.z > 0 && distance < best) { nearest = pair.Key; best = distance; }
            }
            return nearest;
        }

        void Sync()
        {
            var roster = economy.Hubs;
            var retained = new HashSet<string>();
            foreach (var hub in roster)
            {
                if (hub.Pos == null) continue;
                retained.Add(hub.Id);
                if (!hubs.TryGetValue(hub.Id, out var visual))
                {
                    var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    marker.transform.SetParent(transform, false);
                    Destroy(marker.GetComponent<Collider>());
                    visual = new HubVisual { Marker = marker, Renderer = marker.GetComponent<Renderer>() };
                    visual.Renderer.sharedMaterial = material;
                    hubs.Add(hub.Id, visual);
                }
                visual.Id = hub.Id;
                visual.Name = hub.Name;
                visual.Marker.name = hub.Name;
                var coordinate = new GeoCoordinate(hub.Pos.Lat, hub.Pos.Lon);
                var position = GeoMath.SurfaceNormal(coordinate) * (float)((GeoMath.EarthRadiusMeters
                    + globe.SampleHeightMeters(coordinate, SampleLod()) + 35) / GeoMath.MetersPerUnityUnit);
                visual.Marker.transform.position = position;
                visual.Marker.transform.rotation = Quaternion.FromToRotation(Vector3.up, position.normalized);
            }
            foreach (var key in hubs.Keys)
            {
                if (retained.Contains(key)) continue;
                Destroy(hubs[key].Marker);
                removed.Add(key);
            }
            foreach (var key in removed) hubs.Remove(key);
            removed.Clear();
        }

        /// <summary>Relief LOD sampled per altitude, matching WorldCartography: fine close up, coarse from orbit.</summary>
        int SampleLod()
        {
            if (store == null) return 4;
            var altitude = store.State.AltitudeMeters;
            if (altitude > 2_000_000) return 0;
            return altitude > 100_000 ? 2 : 4;
        }

        void OnDestroy()
        {
            if (economy != null) economy.Changed -= OnEconomyChanged;
            foreach (var visual in hubs.Values) if (visual.Marker != null) Destroy(visual.Marker);
            hubs.Clear();
            if (material != null) Destroy(material);
        }
    }
}
