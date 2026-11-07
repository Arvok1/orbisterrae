using System;
using System.IO;
using System.Threading.Tasks;
using Orbis.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.World
{
    public sealed class GlobeRenderer : MonoBehaviour
    {
        const float TerrainExaggeration = 1.45f;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");

        Material material;
        Mesh globeMesh;
        Mesh patchMesh;
        GameObject patchObject;
        Texture2D globalTexture;
        WorldPackage package;
        WorldHeightTile currentTile;
        WorldTileKey requestedKey;
        Task<WorldHeightTile> tileLoad;
        bool hasRequestedKey;

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

            var packageRoot = ResolvePackageRoot(config.TerrainPackageRoot);
            if (!string.IsNullOrEmpty(packageRoot))
            {
                try
                {
                    package = new WorldPackage(packageRoot);
                    PackageStatus = $"orbis-world-v2 LOD {package.Manifest.max_lod}";
                    globalTexture = LoadTexture(package.ResolveVisual(package.Manifest.visual?.relief));
                }
                catch (Exception exception)
                {
                    PackageStatus = $"Pacote inválido: {exception.Message}";
                    Debug.LogError(PackageStatus);
                }
            }
            if (globalTexture == null)
            {
                var embedded = Path.Combine(Application.streamingAssetsPath, "world-v2", "global-relief.png");
                globalTexture = LoadTexture(embedded) ?? Texture2D.grayTexture;
                if (string.IsNullOrEmpty(PackageStatus))
                    PackageStatus = "Pacote externo ausente; usando mapa global de segurança.";
            }
            material.SetTexture(BaseMap, globalTexture);
            SetVisualStyle(MapVisualStyle.Relief);
        }

        public void SetView(GeoCoordinate focus, double altitudeMeters)
        {
            if (package == null) return;
            var desiredLod = DesiredLod(altitudeMeters, package.Manifest.max_lod);
            var normal = GeoMath.SurfaceNormal(focus);
            var key = CubeSphereProjection.TileFor(normal, desiredLod, out _);
            if ((!hasRequestedKey || !requestedKey.Equals(key)) && tileLoad == null)
            {
                requestedKey = key;
                hasRequestedKey = true;
                tileLoad = Task.Run(() => package.ReadTile(key));
            }
            if (tileLoad == null || !tileLoad.IsCompleted) return;
            if (tileLoad.IsFaulted)
            {
                Debug.LogError($"Falha ao carregar tile {requestedKey}: {tileLoad.Exception?.GetBaseException().Message}");
                tileLoad = null;
                return;
            }
            var loaded = tileLoad.Result;
            tileLoad = null;
            if (!loaded.Key.Equals(key)) { hasRequestedKey = false; return; }
            if (currentTile != null && currentTile.Key.Equals(loaded.Key)) return;
            currentTile = loaded;
            ApplyPatch(loaded);
        }

        public float SampleHeightMeters(GeoCoordinate coordinate)
        {
            if (currentTile == null) return 0f;
            var selected = CubeSphereProjection.TileFor(GeoMath.SurfaceNormal(coordinate), currentTile.Key.Lod, out var uv);
            return selected.Equals(currentTile.Key) ? Mathf.Max(0f, currentTile.Sample(uv.x, uv.y)) * TerrainExaggeration : 0f;
        }

        public void SetVisualStyle(MapVisualStyle style)
        {
            VisualStyle = style;
            if (material == null) return;
            var color = style == MapVisualStyle.Manuscript
                ? new Color(0.78f, 0.62f, 0.39f)
                : new Color(0.78f, 0.86f, 0.72f);
            material.SetColor(BaseColor, color);
            material.SetFloat(Metallic, 0f);
            material.SetFloat(Smoothness, style == MapVisualStyle.Manuscript ? 0.02f : 0.13f);
        }

        void ApplyPatch(WorldHeightTile tile)
        {
            var nextMesh = CreatePatchMesh(tile);
            if (patchObject == null)
            {
                patchObject = new GameObject("Active terrain patch");
                patchObject.transform.SetParent(transform, false);
                patchObject.AddComponent<MeshFilter>();
                var renderer = patchObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
            }
            patchObject.GetComponent<MeshFilter>().sharedMesh = nextMesh;
            if (patchMesh != null) Destroy(patchMesh);
            patchMesh = nextMesh;
        }

        static int DesiredLod(double altitudeMeters, int maximumLod)
        {
            if (altitudeMeters >= 2_000_000) return 0;
            var lod = 1 + (int)Math.Floor(Math.Log(2_000_000 / Math.Max(20, altitudeMeters), 4));
            return Math.Clamp(lod, 0, maximumLod);
        }

        static string ResolvePackageRoot(string configured)
        {
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, "manifest.json"))) return configured;
            var streaming = Path.Combine(Application.streamingAssetsPath, "world-v2");
            return File.Exists(Path.Combine(streaming, "manifest.json")) ? streaming : string.Empty;
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
                    var longitudeRadians = Mathf.PI * 2f * longitude / longitudeSegments;
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
            if (globalTexture != null) Destroy(globalTexture);
            if (globeMesh != null) Destroy(globeMesh);
            if (patchMesh != null) Destroy(patchMesh);
        }
    }
}
