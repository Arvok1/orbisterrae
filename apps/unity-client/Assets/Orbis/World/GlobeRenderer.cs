using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Orbis.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.World
{
    public sealed class GlobeRenderer : MonoBehaviour
    {
        const float TerrainExaggeration = 2.2f;
        const float PatchFadeSeconds = 0.4f;
        const float PatchUnloadSpeed = 2f;
        const int ConcurrentTileLoads = 2;
        const int SampleCacheLimit = 512;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int Fade = Shader.PropertyToID("_Fade");

        sealed class ReliefPatch
        {
            public WorldTileKey Key;
            public GameObject Object;
            public Mesh Mesh;
            public float Fade;
            public bool Retiring;
            public MaterialPropertyBlock Properties;
        }

        Material material;
        Mesh globeMesh;
        Texture2D globalTexture;
        Texture2D biomeTexture;
        WorldPackage package;
        readonly Dictionary<WorldTileKey, ReliefPatch> patches = new();
        readonly Dictionary<WorldTileKey, Task<WorldHeightTile>> tileLoads = new();
        readonly Dictionary<WorldTileKey, WorldHeightTile> tileCache = new();
        readonly Queue<WorldTileKey> tileOrder = new();
        int viewLod = -1;
        float retryAfter;
        public int TerrainRevision { get; private set; }
        public string HydrographyPath { get; private set; }
        public string CoastlinePath { get; private set; }

        public Collider SurfaceCollider { get; private set; }
        public MapVisualStyle VisualStyle { get; private set; }
        public string PackageStatus { get; private set; } = string.Empty;

        public void Initialize(ClientRuntimeConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var filter = gameObject.AddComponent<MeshFilter>();
            var meshRenderer = gameObject.AddComponent<MeshRenderer>();
            globeMesh = CreateGlobeMesh(256, 128);
            filter.sharedMesh = globeMesh;
            SurfaceCollider = gameObject.AddComponent<SphereCollider>();
            ((SphereCollider)SurfaceCollider).radius = (float)GeoMath.EarthRadiusUnity;
            material = CreateMaterial("Orbis Globe Runtime");
            meshRenderer.sharedMaterial = material;
            // O fade do mosaico é aplicado via MaterialPropertyBlock; a malha base do globo nasce em _Fade = 1.
            material.SetFloat(Fade, 1f);

            var (packageRoot, packageFallbacks) = PackageRoots.Select(
                config.TerrainPackageRoot, Path.Combine(Application.streamingAssetsPath, "world-v2"));
            if (!string.IsNullOrEmpty(packageRoot))
            {
                try
                {
                    package = new WorldPackage(packageRoot, packageFallbacks);
                    PackageStatus = $"orbis-world-v2 LOD {package.Manifest.max_lod}";
                    globalTexture = LoadTexture(package.ResolveVisual(package.Manifest.visual?.relief));
                    biomeTexture = LoadTexture(package.ResolveVisual(package.Manifest.visual?.biomes));
                    HydrographyPath = package.ResolveVisual(package.Manifest.visual?.hydrography);
                    CoastlinePath = package.ResolveVisual(package.Manifest.visual?.coastlines);
                }
                catch (Exception exception)
                {
                    PackageStatus = "terrain_invalid";
                    package = null;
                    if(globalTexture!=null)Destroy(globalTexture);
                    if(biomeTexture!=null)Destroy(biomeTexture);
                    globalTexture=null;biomeTexture=null;
                    Debug.LogWarning($"{PackageStatus}: {exception.Message}");
                }
            }
            if (globalTexture == null)
            {
                var embedded = Path.Combine(Application.streamingAssetsPath, "world-v2", "global-relief.png");
                globalTexture = LoadTexture(embedded) ?? Texture2D.grayTexture;
                biomeTexture = LoadTexture(Path.Combine(Application.streamingAssetsPath, "world-v2", "global-biomes.png"));
                HydrographyPath = Path.Combine(Application.streamingAssetsPath, "world-v2", "hydrography.json");
                CoastlinePath = Path.Combine(Application.streamingAssetsPath, "world-v2", "coastlines.json");
                if (string.IsNullOrEmpty(PackageStatus))
                    PackageStatus = globalTexture==Texture2D.grayTexture?"terrain_missing":"terrain_fallback";
            }
            material.SetTexture(BaseMap, globalTexture);
            material.SetTexture("_BiomeMap", biomeTexture != null ? biomeTexture : Texture2D.blackTexture);
            SetVisualStyle(MapVisualStyle.Relief);
        }

        public void SetView(GeoCoordinate focus, double altitudeMeters)
        {
            if (package == null) return;
            var lod = DesiredLod(altitudeMeters, package.Manifest.max_lod);
            var normal = GeoMath.SurfaceNormal(focus);
            var center = CubeSphereProjection.TileFor(normal, lod, out _);
            var ring = CubeSphereProjection.MosaicRing(center);
            if (lod != viewLod)
            {
                viewLod = lod;
                foreach (var pair in patches) pair.Value.Retiring = true;
            }
            RetireOutsideRing(ring);
            ScheduleTiles(ring);
            PumpCompletedTiles(center, ring);
            TickPatches();
        }

        void RetireOutsideRing(List<WorldTileKey> ring)
        {
            foreach (var pair in patches)
            {
                if (!pair.Value.Retiring && !ring.Contains(pair.Key)) pair.Value.Retiring = true;
            }
        }

        void ScheduleTiles(List<WorldTileKey> ring)
        {
            if (Time.unscaledTime < retryAfter) return;
            foreach (var key in ring)
            {
                if (patches.ContainsKey(key) || tileLoads.ContainsKey(key)) continue;
                if (tileLoads.Count >= ConcurrentTileLoads) return;
                try { tileLoads.Add(key, Task.Run(() => package.ReadTile(key))); }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Falha ao agendar tile {key}: {exception.Message}");
                    return;
                }
            }
        }

        void PumpCompletedTiles(WorldTileKey center, List<WorldTileKey> ring)
        {
            if (tileLoads.Count == 0) return;
            List<WorldTileKey> completed = null;
            foreach (var pair in tileLoads)
            {
                if (!pair.Value.IsCompleted) continue;
                (completed ??= new List<WorldTileKey>()).Add(pair.Key);
            }
            if (completed == null) return;
            foreach (var key in completed)
            {
                var load = tileLoads[key];
                tileLoads.Remove(key);
                if (!ring.Contains(key)) continue;
                WorldHeightTile tile;
                try { tile = load.Result; }
                catch (Exception exception)
                {
                    retryAfter = Time.unscaledTime + 2f;
                    Debug.LogWarning($"Falha ao carregar tile {key}: {exception.Message}");
                    continue;
                }
                StoreTile(tile);
                if (key.Equals(center)) TerrainRevision++;
                AttachPatch(tile);
            }
        }

        void AttachPatch(WorldHeightTile tile)
        {
            if (patches.TryGetValue(tile.Key, out var existing))
            {
                if (existing.Mesh != null) Destroy(existing.Mesh);
                existing.Mesh = CreatePatchMesh(tile);
                existing.Object.GetComponent<MeshFilter>().sharedMesh = existing.Mesh;
                existing.Retiring = false;
                return;
            }
            var patch = new ReliefPatch { Key = tile.Key, Fade = 0f, Properties = new MaterialPropertyBlock() };
            patch.Object = new GameObject($"Terrain {tile.Key}");
            patch.Object.transform.SetParent(transform, false);
            patch.Mesh = CreatePatchMesh(tile);
            patch.Object.AddComponent<MeshFilter>().sharedMesh = patch.Mesh;
            patch.Object.AddComponent<MeshRenderer>().sharedMaterial = material;
            patches.Add(tile.Key, patch);
        }

        void TickPatches()
        {
            if (patches.Count == 0) return;
            List<WorldTileKey> finished = null;
            foreach (var pair in patches)
            {
                var patch = pair.Value;
                var step = Time.unscaledDeltaTime / PatchFadeSeconds * (patch.Retiring ? PatchUnloadSpeed : 1f);
                patch.Fade = Mathf.Clamp01(patch.Fade + step);
                if (patch.Object == null) continue;
                patch.Properties.SetFloat(Fade, patch.Fade);
                patch.Object.GetComponent<MeshRenderer>().SetPropertyBlock(patch.Properties);
                if (!patch.Retiring || patch.Fade > 0f) continue;
                (finished ??= new List<WorldTileKey>()).Add(pair.Key);
            }
            if (finished == null) return;
            foreach (var key in finished) ReleasePatch(key);
        }

        void ReleasePatch(WorldTileKey key)
        {
            if (!patches.TryGetValue(key, out var patch)) return;
            patches.Remove(key);
            if (patch.Object != null) Destroy(patch.Object);
            if (patch.Mesh != null) Destroy(patch.Mesh);
        }

        public float SampleHeightMeters(GeoCoordinate coordinate)
        {
            if (package == null || viewLod < 0) return 0f;
            var key = CubeSphereProjection.TileFor(GeoMath.SurfaceNormal(coordinate), viewLod, out var uv);
            if (!TryReadTile(key, out var tile)) return 0f;
            return Mathf.Max(0f, tile.Sample(uv.x, uv.y)) * TerrainExaggeration;
        }

        /// <summary>Relief sampled from the streamed package at a fixed LOD, without shifting the live patch.</summary>
        public float SampleHeightMeters(GeoCoordinate coordinate, int lod)
        {
            if (package == null) return 0f;
            var effective = Math.Clamp(lod, 0, package.Manifest.max_lod);
            var key = CubeSphereProjection.TileFor(GeoMath.SurfaceNormal(coordinate), effective, out var uv);
            if (!TryReadTile(key, out var tile)) return 0f;
            return Mathf.Max(0f, tile.Sample(uv.x, uv.y)) * TerrainExaggeration;
        }

        public bool TryReadTile(WorldTileKey key, out WorldHeightTile tile)
        {
            if (package == null) { tile = null; return false; }
            if (tileCache.TryGetValue(key, out tile)) return true;
            try
            {
                tile = package.ReadTile(key);
            }
            catch (Exception)
            {
                tile = null;
                return false;
            }
            StoreTile(tile);
            return true;
        }

        /// <summary>Caches a tile for synchronous relief sampling, evicting the oldest samples FIFO.</summary>
        void StoreTile(WorldHeightTile tile)
        {
            if (tileCache.ContainsKey(tile.Key)) return;
            while (tileCache.Count >= SampleCacheLimit)
            {
                var oldest = tileOrder.Dequeue();
                if (tileCache.TryGetValue(oldest, out var stale)) tileCache.Remove(stale.Key);
            }
            tileOrder.Enqueue(tile.Key);
            tileCache[tile.Key] = tile;
        }

        public void SetVisualStyle(MapVisualStyle style)
        {
            VisualStyle = style;
            if (material == null) return;
            var color = style == MapVisualStyle.Manuscript
                ? new Color(0.78f, 0.62f, 0.39f)
                : new Color(0.78f, 0.86f, 0.72f);
            material.SetColor(BaseColor, color);
            material.SetFloat("_Manuscript", style == MapVisualStyle.Manuscript ? 1 : 0);
        }

        static int DesiredLod(double altitudeMeters, int maximumLod)
        {
            if (altitudeMeters >= 2_000_000) return 0;
            var lod = 1 + (int)Math.Floor(Math.Log(2_000_000 / Math.Max(20, altitudeMeters), 4));
            return Math.Clamp(lod, 0, maximumLod);
        }

        static Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false) { name = "Orbis global relief" };
                if (!texture.LoadImage(File.ReadAllBytes(path), false))
                {
                    Destroy(texture);
                    return null;
                }
                texture.wrapModeU = TextureWrapMode.Repeat;
                texture.wrapModeV = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Trilinear;
                texture.anisoLevel = 8;
                return texture;
            }
            catch (Exception exception)
            {
                Debug.LogError($"Falha ao ler textura global '{path}': {exception.Message}");
                return null;
            }
        }

        static Texture2D CreateFallbackTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, true, false) { name = "Orbis fallback continents" };
            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
            {
                var latitude = (y / (float)(height - 1) - 0.5f) * 180f;
                for (var x = 0; x < width; x++)
                {
                    var longitude = x / (float)(width - 1) * 360f - 180f;
                    var land = ContinentalField(longitude, latitude) > 0.54f;
                    var polar = Mathf.SmoothStep(0f, 1f, (Mathf.Abs(latitude) - 48f) / 35f);
                    pixels[y * width + x] = land
                        ? Color.Lerp(new Color(0.16f, 0.25f, 0.13f), new Color(0.65f, 0.62f, 0.48f), polar)
                        : Color.Lerp(new Color(0.035f, 0.12f, 0.16f), new Color(0.11f, 0.23f, 0.27f), polar);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            texture.wrapModeU = TextureWrapMode.Repeat;
            texture.wrapModeV = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            return texture;
        }

        static float ContinentalField(float longitude, float latitude)
        {
            var value = Blob(longitude, latitude, -105, 45, 50, 33);
            value = Mathf.Max(value, Blob(longitude, latitude, -62, -16, 26, 46));
            value = Mathf.Max(value, Blob(longitude, latitude, 18, 7, 35, 45));
            value = Mathf.Max(value, Blob(longitude, latitude, 72, 43, 78, 38));
            value = Mathf.Max(value, Blob(longitude, latitude, 134, -25, 26, 19));
            value = Mathf.Max(value, Blob(longitude, latitude, -42, 72, 25, 14));
            value = Mathf.Max(value, Mathf.InverseLerp(62f, 90f, -latitude));
            return value;
        }

        static float Blob(float longitude, float latitude, float centerLongitude, float centerLatitude, float radiusX, float radiusY)
        {
            var longitudeDistance = Mathf.DeltaAngle(centerLongitude, longitude) / radiusX;
            var latitudeDistance = (latitude - centerLatitude) / radiusY;
            return 1f - Mathf.Sqrt(longitudeDistance * longitudeDistance + latitudeDistance * latitudeDistance);
        }

        Material CreateMaterial(string materialName)
        {
            var cartographyShader = Resources.Load<Shader>("Orbis/Cartography");
            if (cartographyShader != null) return new Material(cartographyShader) { name = materialName };
            var resourceMaterial = Resources.Load<Material>("Orbis/WorldRelief");
            var pipelineMaterial = GraphicsSettings.currentRenderPipeline == null ? null : GraphicsSettings.currentRenderPipeline.defaultMaterial;
            if (resourceMaterial != null) return new Material(resourceMaterial) { name = materialName };
            if (pipelineMaterial != null) return new Material(pipelineMaterial) { name = materialName };
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No compatible world material was found.");
            return new Material(shader) { name = materialName };
        }

        static Mesh CreateGlobeMesh(int longitudeSegments, int latitudeSegments)
        {
            var vertices = new Vector3[(longitudeSegments + 1) * (latitudeSegments + 1)];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[longitudeSegments * latitudeSegments * 6];
            var radius = (float)GeoMath.EarthRadiusUnity;
            var vertexIndex = 0;
            for (var latitude = 0; latitude <= latitudeSegments; latitude++)
            {
                var latitudeRadians = Mathf.PI * latitude / latitudeSegments - Mathf.PI * 0.5f;
                var cosLatitude = Mathf.Cos(latitudeRadians);
                var sinLatitude = Mathf.Sin(latitudeRadians);
                for (var longitude = 0; longitude <= longitudeSegments; longitude++)
                {
                    var longitudeRadians = Mathf.PI * 2f * longitude / longitudeSegments - Mathf.PI;
                    var normal = new Vector3(cosLatitude * Mathf.Sin(longitudeRadians), sinLatitude, cosLatitude * Mathf.Cos(longitudeRadians));
                    vertices[vertexIndex] = normal * radius;
                    normals[vertexIndex] = normal;
                    var mercator = 0.5f - Mathf.Log(Mathf.Tan(Mathf.PI * 0.25f + latitudeRadians * 0.5f)) / (2f * Mathf.PI);
                    uv[vertexIndex] = new Vector2((float)longitude / longitudeSegments, 1f - Mathf.Clamp01(mercator));
                    vertexIndex++;
                }
            }
            FillGridTriangles(triangles, longitudeSegments, latitudeSegments, longitudeSegments + 1);
            var mesh = new Mesh { name = "Orbis global globe", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreatePatchMesh(WorldHeightTile tile)
        {
            var side = tile.Side;
            var vertices = new Vector3[side * side];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(side - 1) * (side - 1) * 6];
            for (var y = 0; y < side; y++)
            {
                for (var x = 0; x < side; x++)
                {
                    var index = y * side + x;
                    var direction = CubeSphereProjection.Direction(tile.Key, x / (float)(side - 1), y / (float)(side - 1));
                    var height = Mathf.Max(0f, tile.Heights[index]) * TerrainExaggeration / (float)GeoMath.MetersPerUnityUnit;
                    vertices[index] = direction * ((float)GeoMath.EarthRadiusUnity + height + 0.002f);
                    var longitude = Mathf.Atan2(direction.x, direction.z);
                    var latitude = Mathf.Asin(direction.y);
                    var mercator = 0.5f - Mathf.Log(Mathf.Tan(Mathf.PI * 0.25f + latitude * 0.5f)) / (2f * Mathf.PI);
                    uv[index] = new Vector2(Mathf.Repeat(longitude / (2f * Mathf.PI) + 0.5f, 1f), 1f - Mathf.Clamp01(mercator));
                }
            }
            FillGridTriangles(triangles, side - 1, side - 1, side);
            var mesh = new Mesh { name = $"Terrain {tile.Key}", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void FillGridTriangles(int[] triangles, int width, int height, int stride)
        {
            var triangleIndex = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var current = y * stride + x;
                    var next = current + stride;
                    triangles[triangleIndex++] = current;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = current + 1;
                    triangles[triangleIndex++] = current + 1;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = next + 1;
                }
            }
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (globalTexture != null && globalTexture != Texture2D.grayTexture) Destroy(globalTexture);
            if (biomeTexture != null) Destroy(biomeTexture);
            if (globeMesh != null) Destroy(globeMesh);
            List<WorldTileKey> stale = null;
            foreach (var pair in patches) (stale ??= new List<WorldTileKey>()).Add(pair.Key);
            if (stale != null)
                foreach (var key in stale)
                {
                    patches.TryGetValue(key, out var patch);
                    if (patch.Object != null) Destroy(patch.Object);
                    if (patch.Mesh != null) Destroy(patch.Mesh);
                    patches.Remove(key);
                }
        }
    }
}
