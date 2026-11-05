using System;
using Orbis.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.World
{
    public sealed class GlobeRenderer : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Metallic = Shader.PropertyToID("_Metallic");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");

        Material material;
        MeshRenderer meshRenderer;

        public Collider SurfaceCollider { get; private set; }
        public MapVisualStyle VisualStyle { get; private set; }

        public void Initialize()
        {
            var filter = gameObject.AddComponent<MeshFilter>();
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = CreateGlobeMesh(128, 64);
            SurfaceCollider = gameObject.AddComponent<SphereCollider>();
            ((SphereCollider)SurfaceCollider).radius = (float)GeoMath.EarthRadiusUnity;

            var resourceMaterial = Resources.Load<Material>("Orbis/WorldRelief");
            var pipelineMaterial = GraphicsSettings.currentRenderPipeline == null
                ? null
                : GraphicsSettings.currentRenderPipeline.defaultMaterial;
            if (resourceMaterial != null)
            {
                material = new Material(resourceMaterial) { name = "Orbis Globe Runtime" };
            }
            else if (pipelineMaterial != null)
            {
                material = new Material(pipelineMaterial) { name = "Orbis Globe Runtime" };
            }
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader == null) throw new InvalidOperationException("No compatible world material was found.");
                material = new Material(shader) { name = "Orbis Globe Runtime" };
            }
            meshRenderer.sharedMaterial = material;
            SetVisualStyle(MapVisualStyle.Relief);
        }

        public void SetVisualStyle(MapVisualStyle style)
        {
            VisualStyle = style;
            if (material == null) return;

            if (style == MapVisualStyle.Manuscript)
            {
                material.SetColor(BaseColor, new Color(0.36f, 0.29f, 0.19f));
                material.SetFloat(Metallic, 0f);
                material.SetFloat(Smoothness, 0.04f);
            }
            else
            {
                material.SetColor(BaseColor, new Color(0.075f, 0.16f, 0.12f));
                material.SetFloat(Metallic, 0f);
                material.SetFloat(Smoothness, 0.18f);
            }
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
                    var normal = new Vector3(
                        cosLatitude * Mathf.Sin(longitudeRadians),
                        sinLatitude,
                        cosLatitude * Mathf.Cos(longitudeRadians));
                    vertices[vertexIndex] = normal * radius;
                    normals[vertexIndex] = normal;
                    uv[vertexIndex] = new Vector2((float)longitude / longitudeSegments, (float)latitude / latitudeSegments);
                    vertexIndex++;
                }
            }

            var triangleIndex = 0;
            for (var latitude = 0; latitude < latitudeSegments; latitude++)
            {
                for (var longitude = 0; longitude < longitudeSegments; longitude++)
                {
                    var current = latitude * (longitudeSegments + 1) + longitude;
                    var next = current + longitudeSegments + 1;
                    triangles[triangleIndex++] = current;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = current + 1;
                    triangles[triangleIndex++] = current + 1;
                    triangles[triangleIndex++] = next;
                    triangles[triangleIndex++] = next + 1;
                }
            }

            var mesh = new Mesh { name = "Orbis Globe 128x64", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
