using Orbis.Core;
using Orbis.V1;
using UnityEngine;

namespace Orbis.World
{
    public sealed class CityWorldRenderer : MonoBehaviour
    {
        GameStore store;
        GlobeRenderer globe;
        GameObject cityRoot;
        string renderedKey;
        Mesh surfaceMesh;
        Texture2D surfaceTexture;
        Material surfaceMaterial;
        public void Initialize(GameStore gameStore, GlobeRenderer terrain) { store = gameStore; globe = terrain; }
        void LateUpdate()
        {
            var state = store.State;
            var city = state.City;
            var location = CityPresentation.FindCity(state, state.SelectedCityId);
            var visible = city != null && location?.Pos != null && state.AltitudeMeters < 30000;
            if (cityRoot != null) cityRoot.SetActive(visible);
            if (!visible) return;
            var origin = new GeoCoordinate(location.Pos.Lat, location.Pos.Lon);
            var height = globe.SampleHeightMeters(origin);
            var key = $"{city.CityId}:{city.Revision}:{height}";
            if (key == renderedKey) return;
            renderedKey = key;
            if (cityRoot != null) Destroy(cityRoot);
            cityRoot = new GameObject("Authoritative city");
            cityRoot.transform.SetParent(transform, false);
            var normal = GeoMath.SurfaceNormal(origin);
            var east = Vector3.Cross(Vector3.up, normal).normalized;
            if (east.sqrMagnitude < .01f) east = Vector3.right;
            var north = Vector3.Cross(normal, east).normalized;
            cityRoot.transform.position = normal * (float)((GeoMath.EarthRadiusMeters + height + 1) / GeoMath.MetersPerUnityUnit);
            cityRoot.transform.rotation = Quaternion.LookRotation(-north, normal);
            try
            {
                var count = checked((int)(city.Width * city.Height));
                var roads = CityPresentation.DecodeRle(city.RoadsRle.ToByteArray(), count);
                var zones = CityPresentation.DecodeRle(city.ZonesRle.ToByteArray(), count);
                if (surfaceMesh != null) Destroy(surfaceMesh);
                if (surfaceTexture != null) Destroy(surfaceTexture);
                if (surfaceMaterial != null) Destroy(surfaceMaterial);
                surfaceTexture = new Texture2D((int)city.Width, (int)city.Height, TextureFormat.RGB24, false);
                surfaceTexture.filterMode = FilterMode.Point;
                surfaceTexture.wrapMode = TextureWrapMode.Clamp;
                var pixels = new Color[count];
                for (var index = 0; index < count; index++)
                {
                    pixels[index] = roads[index] != 0 ? new Color(.45f,.38f,.27f)
                        : zones[index] != 0 ? new Color(.27f,.37f,.21f) : new Color(.32f,.34f,.23f);
                }
                surfaceTexture.SetPixels(pixels);
                surfaceTexture.Apply();
                var ground = new GameObject("City surface");
                ground.transform.SetParent(cityRoot.transform, false);
                var halfWidth = city.Width * .005f;
                var halfHeight = city.Height * .005f;
                surfaceMesh = new Mesh { vertices = new[] { new Vector3(-halfWidth,0,-halfHeight), new Vector3(-halfWidth,0,halfHeight),
                    new Vector3(halfWidth,0,halfHeight), new Vector3(halfWidth,0,-halfHeight) },
                    uv = new[] { new Vector2(0,0),new Vector2(0,1),new Vector2(1,1),new Vector2(1,0) }, triangles = new[] { 0,1,2,0,2,3 } };
                surfaceMesh.RecalculateNormals();
                ground.AddComponent<MeshFilter>().sharedMesh = surfaceMesh;
                surfaceMaterial = new Material(Resources.Load<Material>("Orbis/WorldRelief"));
                surfaceMaterial.SetTexture("_BaseMap", surfaceTexture);
                ground.AddComponent<MeshRenderer>().sharedMaterial = surfaceMaterial;
                foreach (var building in city.Buildings)
                {
                    BuildingDefinition definition = null;
                    foreach (var candidate in city.Definitions) if (candidate.Id == building.BuildingId) definition = candidate;
                    if (definition == null) continue;
                    var footprint = CityPresentation.Footprint(definition, building.Orientation);
                    var position = new Vector3((building.X + footprint.x / 2f - city.Width / 2f) * .01f, 0,
                        (building.Y + footprint.y / 2f - city.Height / 2f) * .01f);
                    var size = new Vector3(footprint.x * .008f, building.Complete ? .006f : .001f, footprint.y * .008f);
                    Part(building.BuildingId, position + Vector3.up * size.y / 2, size, new Color(.55f,.46f,.32f));
                    if (!building.Complete) continue;
                    Part("Roof", position + Vector3.up * .007f, new Vector3(size.x * 1.1f,.002f,size.z * 1.1f), new Color(.28f,.16f,.11f));
                    Part("Door", position + new Vector3(0,.002f,-size.z/2), new Vector3(.0015f,.004f,.0003f), new Color(.16f,.11f,.07f));
                }
            }
            catch (System.ArgumentException exception) { Debug.LogWarning(exception.Message); }
        }
        void Part(string label, Vector3 position, Vector3 scale, Color color)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = label;
            part.transform.SetParent(cityRoot.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            Destroy(part.GetComponent<Collider>());
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            part.GetComponent<Renderer>().SetPropertyBlock(block);
        }
        void OnDestroy()
        {
            if (cityRoot != null) Destroy(cityRoot);
            if (surfaceMesh != null) Destroy(surfaceMesh);
            if (surfaceTexture != null) Destroy(surfaceTexture);
            if (surfaceMaterial != null) Destroy(surfaceMaterial);
        }
    }
}
