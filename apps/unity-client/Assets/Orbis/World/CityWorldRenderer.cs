using System;
using System.Collections.Generic;
using System.Linq;
using Orbis.Core;
using Orbis.V1;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.World
{
    public sealed class CityWorldRenderer : MonoBehaviour
    {
        sealed class Instance
        {
            public string Signature;
            public string Role;
            public uint Variant;
            public bool Complete;
            public Matrix4x4 Local;
            public int Sector;
        }
        sealed class Batch
        {
            public Mesh Mesh;
            public Matrix4x4[] Local;
            public Matrix4x4[] World;
            public Bounds Bounds;
        }
        readonly Dictionary<string, Instance> instances = new();
        readonly Dictionary<string, Mesh> meshCache = new();
        readonly HashSet<Mesh> ownedMeshes = new();
        readonly List<Batch> batches = new();
        Material[] materials;
        GameStore store;
        GlobeRenderer globe;
        Camera worldCamera;
        GameObject ground;
        Mesh groundMesh;
        Texture2D surface;
        Material groundMaterial;
        CitySnapshot snapshot;
        string cityId;
        string surfaceKey;
        int currentLod = -1;
        Matrix4x4 lastFrame;
        readonly Plane[] frustum = new Plane[6];
        public int InstanceCount => instances.Count;
        public int BatchCount => batches.Count;

        public void Initialize(GameStore gameStore, GlobeRenderer terrain)
        {
            store = gameStore; globe = terrain; worldCamera = Camera.main;
            materials = new Material[4];
            for (var index = 0; index < materials.Length; index++)
            {
                materials[index] = new Material(Resources.Load<Material>("Orbis/WorldRelief")) { enableInstancing = true };
                materials[index].SetColor("_BaseColor", UrbanKit.Palette[index]);
            }
        }

        void LateUpdate()
        {
            if (store == null || worldCamera == null) return;
            var state = store.State;
            var city = state.City;
            var location = CityPresentation.FindCity(state, state.SelectedCityId);
            var visible = city != null && city.CityId == state.SelectedCityId && location?.Pos != null && state.AltitudeMeters < 30000;
            if (ground != null) ground.SetActive(visible);
            if (!visible) return;
            var origin = new GeoCoordinate(location.Pos.Lat, location.Pos.Lon);
            var frame = new CityFrame(origin, globe.SampleHeightMeters(origin));
            var lod = state.AltitudeMeters > 12000 ? 2 : state.AltitudeMeters > 3000 ? 1 : 0;
            var dirty = false;
            if (cityId != city.CityId) { cityId = city.CityId; instances.Clear(); surfaceKey = null; dirty = true; }
            if (!ReferenceEquals(snapshot, city))
            {
                snapshot = city;
                dirty |= Sync(city);
            }
            UpdateGround(city, origin);
            if (dirty || currentLod != lod) { currentLod = lod; BuildBatches(); lastFrame = default; }
            if (lastFrame != frame.LocalToWorld)
            {
                foreach (var batch in batches)
                {
                    for (var index = 0; index < batch.Local.Length; index++) batch.World[index] = frame.LocalToWorld * batch.Local[index];
                    var bounds = new Bounds(batch.World[0].MultiplyPoint3x4(batch.Mesh.bounds.center), Vector3.zero);
                    foreach (var matrix in batch.World)
                    {
                        var center = matrix.MultiplyPoint3x4(batch.Mesh.bounds.center);
                        var radius = matrix.MultiplyVector(batch.Mesh.bounds.size).magnitude;
                        bounds.Encapsulate(new Bounds(center, Vector3.one * radius));
                    }
                    batch.Bounds = bounds;
                }
                lastFrame = frame.LocalToWorld;
            }
            if (ground != null)
            {
                ground.transform.SetPositionAndRotation(frame.LocalToWorld.MultiplyPoint3x4(Vector3.zero), frame.LocalToWorld.rotation);
                ground.transform.localScale = Vector3.one * .001f;
            }
            GeometryUtility.CalculateFrustumPlanes(worldCamera,frustum);
            foreach (var batch in batches)
            {
                if (!GeometryUtility.TestPlanesAABB(frustum, batch.Bounds)) continue;
                for (var submesh = 0; submesh < 4; submesh++)
                {
                    if (batch.Mesh.GetIndexCount(submesh) == 0) continue;
                    var parameters = new RenderParams(materials[submesh]) { worldBounds = batch.Bounds, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true };
                    Graphics.RenderMeshInstanced(parameters, batch.Mesh, submesh, batch.World);
                }
            }
        }

        bool Sync(CitySnapshot city)
        {
            var definitions = city.Definitions.ToDictionary(definition => definition.Id);
            var retained = new HashSet<string>();
            var dirty = false;
            foreach (var building in city.Buildings)
            {
                if (!definitions.TryGetValue(building.BuildingId, out var definition)) continue;
                retained.Add(building.Id);
                var signature = $"{building.X}:{building.Y}:{building.Orientation}:{building.Complete}:{definition.VisualRole}:{definition.Width}:{definition.Height}";
                if (instances.TryGetValue(building.Id, out var previous) && previous.Signature == signature) continue;
                var footprint = CityPresentation.Footprint(definition, building.Orientation);
                var center = new Vector3((building.X + footprint.x / 2f - city.Width / 2f) * 10, 0,
                    (building.Y + footprint.y / 2f - city.Height / 2f) * 10);
                instances[building.Id] = new Instance { Signature = signature, Role = definition.VisualRole, Complete = building.Complete,
                    Variant = CityPresentation.Variant(building.Id), Sector = ((int)building.X / 32) + ((int)building.Y / 32) * 1024,
                    Local = Matrix4x4.TRS(center, Quaternion.Euler(0, building.Orientation * 90, 0), new Vector3(definition.Width, 1, definition.Height)) };
                dirty = true;
            }
            foreach (var key in instances.Keys.Where(key => !retained.Contains(key)).ToArray()) { instances.Remove(key); dirty = true; }
            return dirty;
        }

        void BuildBatches()
        {
            batches.Clear();
            foreach (var group in instances.Values.GroupBy(instance => $"{(instance.Complete ? instance.Role : "construction")}:{instance.Variant}:{instance.Sector}"))
            {
                var first = group.First();
                var role = first.Complete ? first.Role : "construction";
                var key = $"{role}-{first.Variant}-lod{currentLod}";
                if (!meshCache.TryGetValue(key, out var mesh))
                {
                    mesh = Resources.Load<Mesh>("Orbis/UrbanKit/" + key);
                    if (mesh == null) { mesh = first.Complete ? UrbanKit.Build(role, (int)first.Variant, currentLod) : UrbanKit.Construction(currentLod); ownedMeshes.Add(mesh); }
                    meshCache.Add(key, mesh);
                }
                var local = group.Select(instance => instance.Local).ToArray();
                for (var start = 0; start < local.Length; start += 500)
                {
                    var part = local.Skip(start).Take(500).ToArray();
                    batches.Add(new Batch { Mesh = mesh, Local = part, World = new Matrix4x4[part.Length] });
                }
            }
        }

        int groundRevision = -1;
        void UpdateGround(CitySnapshot city, GeoCoordinate origin)
        {
            var revision = globe.TerrainRevision;
            var key = city.Width + ":" + city.Height + ":" + city.RoadsRle.ToBase64() + ":" + city.ZonesRle.ToBase64() + ":" + city.TerrainRle.ToBase64();
            if (surfaceKey == key && groundRevision == revision) return;
            try
            {
                var count = checked((int)(city.Width * city.Height));
                var roads = CityPresentation.DecodeRle(city.RoadsRle.ToByteArray(), count);
                var zones = CityPresentation.DecodeRle(city.ZonesRle.ToByteArray(), count);
                var terrain = CityPresentation.DecodeRle(city.TerrainRle.ToByteArray(), count);
                if (surfaceKey != key)
                {
                    if (surface != null) Destroy(surface);
                    surface = new Texture2D((int)city.Width, (int)city.Height, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    var colors = new Color[count];
                    var palette = new[] { new Color(.3f,.33f,.22f),new Color(.49f,.43f,.31f),new Color(.32f,.43f,.2f),
                        new Color(.38f,.32f,.25f),new Color(.52f,.44f,.33f),new Color(.34f,.33f,.29f) };
                    for (var index = 0; index < count; index++) colors[index] = roads[index] != 0 ? new Color(.5f,.43f,.31f)
                        : terrain[index] == 0 ? new Color(.08f,.18f,.22f) : palette[Math.Min(zones[index], palette.Length - 1)];
                    surface.SetPixels(colors); surface.Apply();
                }
                if (ground == null)
                {
                    ground = new GameObject("Authoritative city surface"); ground.transform.SetParent(transform, false);
                    ground.AddComponent<MeshFilter>();
                    groundMaterial = new Material(Resources.Load<Material>("Orbis/WorldRelief"));
                    ground.AddComponent<MeshRenderer>().sharedMaterial = groundMaterial;
                }
                if (groundMesh != null) Destroy(groundMesh);
                // O prato acompanha o relevo do mosaico: os 4 cantos amostram o delta de altitude relativo à origem.
                var halfWidth = city.Width * 5f; var halfHeight = city.Height * 5f;
                var baseElevation = globe.SampleHeightMeters(origin);
                groundMesh = new Mesh { vertices = new[] {
                        new Vector3(-halfWidth, CornerHeight(origin, -halfWidth, -halfHeight, baseElevation), -halfHeight),
                        new Vector3(-halfWidth, CornerHeight(origin, -halfWidth, halfHeight, baseElevation), halfHeight),
                        new Vector3(halfWidth, CornerHeight(origin, halfWidth, halfHeight, baseElevation), halfHeight),
                        new Vector3(halfWidth, CornerHeight(origin, halfWidth, -halfHeight, baseElevation), -halfHeight) },
                    uv = new[] { Vector2.zero,Vector2.up,Vector2.one,Vector2.right },triangles = new[] { 0,1,2,0,2,3 } };
                groundMesh.RecalculateNormals();
                ground.GetComponent<MeshFilter>().sharedMesh = groundMesh;
                groundMaterial.SetTexture("_BaseMap", surface);
                surfaceKey = key;
                groundRevision = revision;
            }
            catch (ArgumentException exception) { Debug.LogWarning(exception.Message); }
        }

        /// <summary>Altitude do relevo do globo no offset geodésico (metros a partir da origem), relativa à elevação de base.</summary>
        float CornerHeight(GeoCoordinate origin, double offsetMetersX, double offsetMetersY, float baseElevationMeters)
        {
            const double MetersPerLatitudeDegree = 111_320;
            var longitudeScale = Math.Max(0.05, Math.Cos(origin.LatitudeDegrees * Math.PI / 180));
            var corner = new GeoCoordinate(
                Mathf.Clamp((float)(origin.LatitudeDegrees + offsetMetersY / MetersPerLatitudeDegree), -89.5f, 89.5f),
                (float)(origin.LongitudeDegrees + offsetMetersX / (MetersPerLatitudeDegree * longitudeScale)));
            return globe.SampleHeightMeters(corner) - baseElevationMeters;
        }
        void OnDestroy()
        {
            foreach (var mesh in ownedMeshes) Destroy(mesh);
            if (materials != null) foreach (var material in materials) Destroy(material);
            if (groundMesh != null) Destroy(groundMesh);
            if (surface != null) Destroy(surface);
            if (groundMaterial != null) Destroy(groundMaterial);
        }
    }
}
