using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Orbis.Core;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Orbis.World
{
    /// <summary>
    /// Draws hydrography (rivers, lakes, coastlines) on the globe.
    /// Geometry samples relief straight from the terrain package at a fixed LOD per
    /// zoom level, is cached per level and is built across frames with a small time
    /// budget, so streaming terrain never forces a single-frame full rebuild.
    /// </summary>
    public sealed class GeographicOverlay : MonoBehaviour
    {
        /// <summary>Maximum milliseconds spent extending overlay geometry per frame.</summary>
        const float BuildBudgetMs = 8f;

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

        /// <summary>Built chunks plus a resumable build cursor for one zoom level.</summary>
        sealed class LevelState
        {
            public int Zoom;
            public int SampleLod;
            public int Sector;
            public int StrokeCursor;
            public int PointCursor = -1;
            public readonly List<GameObject> Chunks = new();
            public readonly List<Vector3> River = new();
            public readonly List<Vector3> Lake = new();
        }

        readonly List<Stroke> strokes = new();
        readonly List<int>[] sectorStrokes = new List<int>[8];
        readonly List<Mesh> meshes = new();
        readonly Dictionary<int, LevelState> levels = new();
        readonly Stopwatch budget = new();
        GlobeRenderer globe;
        GameStore store;
        Material water;
        Material lakeMaterial;
        int level = -1;
        LevelState current;

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
            (sectorStrokes[sector] ??= new List<int>()).Add(strokes.Count - 1);
        }

        void LateUpdate()
        {
            if (store == null) return;
            UpdateLevel();
            if (current == null || current.Sector >= 8) return;
            budget.Restart();
            while (budget.Elapsed.TotalMilliseconds < BuildBudgetMs && current.Sector < 8)
                ExtendLevel(current);
        }

        /// <summary>Switches the active zoom level. Cached levels are kept and re-activated; nothing is rebuilt.</summary>
        void UpdateLevel()
        {
            var nextLevel = store.State.AltitudeMeters > 2000000 ? 2 : store.State.AltitudeMeters > 100000 ? 1 : 0;
            if (nextLevel == level && current != null) return;
            if (current != null) foreach (var chunk in current.Chunks) chunk.SetActive(false);
            level = nextLevel;
            if (!levels.TryGetValue(level, out current))
                current = levels[level] = new LevelState { Zoom = level, SampleLod = SampleLod(level) };
            foreach (var chunk in current.Chunks) chunk.SetActive(true);
        }

        /// <summary>Relief LOD sampled for each zoom level: fine close up, coarse from orbit.</summary>
        static int SampleLod(int level) => level switch { 0 => 4, 1 => 2, _ => 0 };

        void ExtendLevel(LevelState target)
        {
            var list = sectorStrokes[target.Sector];
            if (list == null)
            {
                target.Sector++;
                target.StrokeCursor = 0;
                return;
            }
            var stroke = strokes[list[target.StrokeCursor]];
            var points = stroke.Points;
            var lod = target.SampleLod;
            if (target.PointCursor < 0)
            {
                target.PointCursor = 0;
                if (stroke.Lake)
                {
                    var origin = points[0].LongitudeDegrees;
                    var flat = points.Select(point => new Vector2((float)GeoMath.WrapLongitude(point.LongitudeDegrees-origin),(float)point.LatitudeDegrees)).ToArray();
                    var triangles = PolygonTriangulator.Triangulate(flat);
                    for(var index=0;index<triangles.Length;index+=3)
                    {
                        var first=Position(points[triangles[index]],lod);
                        var second=Position(points[triangles[index+1]],lod);
                        var third=Position(points[triangles[index+2]],lod);
                        if(Vector3.Dot(Vector3.Cross(second-first,third-first),first)<0)(second,third)=(third,second);
                        target.Lake.Add(first);target.Lake.Add(second);target.Lake.Add(third);
                    }
                }
            }
            var stride = 1 << target.Zoom;
            for (var index = target.PointCursor; index < points.Length - 1; index += stride)
            {
                var end = Math.Min(index + stride, points.Length - 1);
                var fromNormal = GeoMath.SurfaceNormal(points[index]);
                var toNormal = GeoMath.SurfaceNormal(points[end]);
                if (!stroke.Lake && InLake(GeoPresentation.FromNormal((fromNormal + toNormal).normalized))) { target.PointCursor = end; continue; }
                var steps = Math.Max(1, (int)Math.Ceiling(Vector3.Angle(fromNormal, toNormal)));
                for (var step = 0; step < steps; step++)
                {
                    target.River.Add(Position(GeoPresentation.FromNormal(Vector3.Slerp(fromNormal, toNormal, step / (float)steps)), lod));
                    target.River.Add(Position(GeoPresentation.FromNormal(Vector3.Slerp(fromNormal, toNormal, (step + 1f) / steps)), lod));
                }
                target.PointCursor = end;
                if (budget.ElapsedMilliseconds >= 20) return;  // resume this stroke next frame
            }
            target.PointCursor = -1;
            target.StrokeCursor++;
            if (target.StrokeCursor >= list.Count) FinishSector(target);
        }

        /// <summary>Turns the buffered vertices of a finished sector into persistent chunks.</summary>
        void FinishSector(LevelState target)
        {
            if (target.Lake.Count > 0)
            {
                var lakeMesh=new Mesh { name="Lakes "+target.Sector,indexFormat=IndexFormat.UInt32 };
                lakeMesh.SetVertices(target.Lake);lakeMesh.SetTriangles(Enumerable.Range(0,target.Lake.Count).ToArray(),0);lakeMesh.RecalculateNormals();lakeMesh.RecalculateBounds();
                AddChunk(target,lakeMesh,true);
            }
            if (target.River.Count > 0)
            {
                AddChunk(target, LineMesh(target.River, "Hydrography " + target.Sector), false);
            }
            target.Sector++;
            target.StrokeCursor = 0;
            target.PointCursor = -1;
            target.River.Clear();target.Lake.Clear();
        }

        void AddChunk(LevelState levelState, Mesh mesh, bool filled)
        {
            meshes.Add(mesh);
            var host=new GameObject(mesh.name);host.transform.SetParent(transform,false);
            host.AddComponent<MeshFilter>().sharedMesh=mesh;
            host.AddComponent<MeshRenderer>().sharedMaterial=filled?lakeMaterial:water;
            host.SetActive(levelState == current);
            levelState.Chunks.Add(host);
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

        Vector3 Position(GeoCoordinate point, int lod) => GeoMath.SurfaceNormal(point)
            * (float)((GeoMath.EarthRadiusMeters + globe.SampleHeightMeters(point, lod) + 12) / GeoMath.MetersPerUnityUnit);

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
