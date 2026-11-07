using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Orbis.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Orbis.World
{
    public sealed class GeographicOverlay : MonoBehaviour
    {
        sealed class Stroke
        {
            public GeoCoordinate[] Points;
            public bool Lake;
            public int Sector;
            public double South;
            public double North;
            public double West;
            public double East;
        }
        readonly List<Stroke> strokes = new();
        readonly List<Mesh> meshes = new();
        readonly List<GameObject> chunks = new();
        readonly Dictionary<int,List<GameObject>> cache=new();
        GlobeRenderer globe;
        GameStore store;
        Material water;
        Material lakeMaterial;
        int revision = -1;
        int level = -1;

        public void Initialize(GameStore gameStore, GlobeRenderer terrain)
        {
            globe = terrain;
            store = gameStore;
            water = new Material(Resources.Load<Shader>("Orbis/CartographicInk"));
            water.SetColor("_BaseColor", new Color(.08f,.17f,.2f));
            lakeMaterial=new Material(water);lakeMaterial.SetFloat("_Ribbon",0);
            if (!File.Exists(globe.HydrographyPath)) return;
            try
            {
                var root = JObject.Parse(File.ReadAllText(globe.HydrographyPath));
                foreach (var river in root["rivers"] ?? new JArray())
                    foreach (var line in river["lines"] ?? new JArray()) AddStroke(line, false);
                foreach (var lake in root["lakes"] ?? new JArray())
                    foreach (var ring in lake["polygons"] ?? new JArray()) AddStroke(ring, true);
                var coast = globe.CoastlinePath;
                if (File.Exists(coast))
                {
                    var land = JObject.Parse(File.ReadAllText(coast));
                    foreach (var polygon in land["coordinates"] ?? new JArray())
                        foreach (var ring in polygon) AddStroke(ring, false);
                }
            }
            catch (Exception exception) { Debug.LogWarning($"Cartography: {exception.Message}"); }
        }

        void AddStroke(JToken line, bool lake)
        {
            var points = line.Select(point => new GeoCoordinate((double)point[1], (double)point[0])).ToArray();
            if (points.Length < 2) return;
            var sector = (int)((points[0].LongitudeDegrees + 180) / 45) % 8;
            if(lake&&points.Length>3&&GeoMath.SurfaceNormal(points[0])==GeoMath.SurfaceNormal(points[points.Length-1]))points=points.Take(points.Length-1).ToArray();
            strokes.Add(new Stroke { Points = points, Lake = lake, Sector = sector,South=points.Min(point=>point.LatitudeDegrees),
                North=points.Max(point=>point.LatitudeDegrees),West=points.Min(point=>point.LongitudeDegrees),East=points.Max(point=>point.LongitudeDegrees) });
        }

        void LateUpdate()
        {
            if (store == null) return;
            var nextLevel = store.State.AltitudeMeters > 2000000 ? 2 : store.State.AltitudeMeters > 100000 ? 1 : 0;
            if (revision == globe.TerrainRevision && nextLevel == level) return;
            if(revision!=globe.TerrainRevision)
            {
                foreach(var chunk in chunks)Destroy(chunk);
                foreach(var mesh in meshes)Destroy(mesh);
                chunks.Clear();meshes.Clear();cache.Clear();
            }
            revision=globe.TerrainRevision;level=nextLevel;
            foreach(var chunk in chunks)chunk.SetActive(false);
            if(cache.TryGetValue(level,out var retained)) { foreach(var chunk in retained)chunk.SetActive(true);return; }
            var firstChunk=chunks.Count;
            for (var sector = 0; sector < 8; sector++)
            {
                var vertices = new List<Vector3>();
                var lakeVertices=new List<Vector3>();
                foreach (var stroke in strokes)
                {
                    if (stroke.Sector != sector) continue;
                    if(stroke.Lake)
                    {
                        var origin=stroke.Points[0].LongitudeDegrees;
                        var flat=stroke.Points.Select(point=>new Vector2((float)GeoMath.WrapLongitude(point.LongitudeDegrees-origin),(float)point.LatitudeDegrees)).ToArray();
                        var triangles=PolygonTriangulator.Triangulate(flat);
                        for(var index=0;index<triangles.Length;index+=3)
                        {
                            var first=Position(stroke.Points[triangles[index]]);
                            var second=Position(stroke.Points[triangles[index+1]]);
                            var third=Position(stroke.Points[triangles[index+2]]);
                            if(Vector3.Dot(Vector3.Cross(second-first,third-first),first)<0)(second,third)=(third,second);
                            lakeVertices.Add(first);lakeVertices.Add(second);lakeVertices.Add(third);
                        }
                    }
                    var stride = 1 << level;
                    for (var index = 0; index < stroke.Points.Length - 1; index += stride)
                    {
                        var end = Math.Min(index + stride, stroke.Points.Length - 1);
                        var from = stroke.Points[index];
                        var to = stroke.Points[end];
                        if (!stroke.Lake && InLake(GeoPresentation.FromNormal((GeoMath.SurfaceNormal(from) + GeoMath.SurfaceNormal(to)).normalized))) continue;
                        var fromNormal = GeoMath.SurfaceNormal(from);
                        var toNormal = GeoMath.SurfaceNormal(to);
                        var steps = Math.Max(1, (int)Math.Ceiling(Vector3.Angle(fromNormal, toNormal)));
                        for (var step = 0; step < steps; step++)
                        {
                            vertices.Add(Position(GeoPresentation.FromNormal(Vector3.Slerp(fromNormal, toNormal, step / (float)steps))));
                            vertices.Add(Position(GeoPresentation.FromNormal(Vector3.Slerp(fromNormal, toNormal, (step + 1f) / steps))));
                        }
                    }
                }
                if(lakeVertices.Count>0)
                {
                    var lakeMesh=new Mesh { name="Lakes "+sector,indexFormat=IndexFormat.UInt32 };
                    lakeMesh.SetVertices(lakeVertices);lakeMesh.SetTriangles(Enumerable.Range(0,lakeVertices.Count).ToArray(),0);lakeMesh.RecalculateNormals();lakeMesh.RecalculateBounds();
                    AddChunk(lakeMesh,true);
                }
                if (vertices.Count == 0) continue;
                var mesh = LineMesh(vertices, "Hydrography " + sector);
                AddChunk(mesh);
            }
            cache[level]=chunks.Skip(firstChunk).ToList();
        }

        void AddChunk(Mesh mesh,bool filled=false)
        {
            meshes.Add(mesh);
            var host=new GameObject(mesh.name);host.transform.SetParent(transform,false);
            host.AddComponent<MeshFilter>().sharedMesh=mesh;
            host.AddComponent<MeshRenderer>().sharedMaterial=filled?lakeMaterial:water;chunks.Add(host);
        }

        bool InLake(GeoCoordinate point)
        {
            foreach (var lake in strokes)
            {
                if (!lake.Lake) continue;
                if(point.LatitudeDegrees<lake.South||point.LatitudeDegrees>lake.North)continue;
                if(lake.East-lake.West<180&&(point.LongitudeDegrees<lake.West||point.LongitudeDegrees>lake.East))continue;
                var inside = false;
                var previous = lake.Points.Length - 1;
                for (var index = 0; index < lake.Points.Length; index++)
                {
                    var from = lake.Points[index]; var to = lake.Points[previous];
                    var fromX = GeoMath.WrapLongitude(from.LongitudeDegrees - point.LongitudeDegrees);
                    var toX = GeoMath.WrapLongitude(to.LongitudeDegrees - point.LongitudeDegrees);
                    if ((from.LatitudeDegrees > point.LatitudeDegrees) != (to.LatitudeDegrees > point.LatitudeDegrees)
                        && 0 < (toX - fromX) * (point.LatitudeDegrees - from.LatitudeDegrees) / (to.LatitudeDegrees - from.LatitudeDegrees) + fromX)
                        inside = !inside;
                    previous = index;
                }
                if (inside) return true;
            }
            return false;
        }

        Vector3 Position(GeoCoordinate point) => GeoMath.SurfaceNormal(point)
            * (float)((GeoMath.EarthRadiusMeters + globe.SampleHeightMeters(point) + 12) / GeoMath.MetersPerUnityUnit);

        public static Mesh LineMesh(List<Vector3> vertices, string label)
        {
            var mesh = new Mesh { name = label, indexFormat = IndexFormat.UInt32 };
            var positions=new List<Vector3>();var endpoints=new List<Vector3>();var sides=new List<Vector2>();var triangles=new List<int>();
            for(var index=0;index+1<vertices.Count;index+=2)
            {
                var start=positions.Count;var from=vertices[index];var to=vertices[index+1];
                positions.AddRange(new[]{from,from,to,to});endpoints.AddRange(new[]{to,to,from,from});
                sides.AddRange(new[]{new Vector2(-1,0),new Vector2(1,0),new Vector2(1,0),new Vector2(-1,0)});
                triangles.AddRange(new[]{start,start+1,start+2,start+1,start+3,start+2});
            }
            mesh.SetVertices(positions);mesh.SetUVs(0,endpoints);mesh.SetUVs(1,sides);mesh.SetTriangles(triangles,0);
            mesh.RecalculateBounds();
            return mesh;
        }
        void OnDestroy() { foreach (var mesh in meshes) Destroy(mesh); if (water != null) Destroy(water);if(lakeMaterial!=null)Destroy(lakeMaterial); }
    }
}
