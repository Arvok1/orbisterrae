using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orbis.Core;
using Orbis.V1;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Orbis.World
{
    public sealed class VisualAcceptanceRunner : MonoBehaviour
    {
        [Serializable] sealed class Sample { public double seconds; public long memory; public int objects; public long drawCalls; }
        [Serializable] sealed class Report
        {
            public string fixture="authorized server snapshots, offline replay";
            public string unity;
            public string gpu;
            public double seconds;
            public double p50FrameMs;
            public double p95FrameMs;
            public Sample[] samples;
        }
        GameStore store;
        GlobeRenderer globe;
        string source;
        string output;
        WorldSnapshot world;
        CitySnapshot[] cities;
        readonly List<double> frames=new();
        readonly List<Sample> samples=new();
        static string Argument(string name)=>Environment.GetCommandLineArgs().FirstOrDefault(value=>value.StartsWith(name+"=",StringComparison.Ordinal))?.Substring(name.Length+1);
        public static bool Requested
        {
            get
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                return !string.IsNullOrEmpty(Argument("--acceptance-export"));
#else
                return false;
#endif
            }
        }
        public void Initialize(GameStore state,GlobeRenderer terrain)
        {
            store=state;globe=terrain;source=Path.GetFullPath(Argument("--acceptance-export"));
            output=Path.GetFullPath(Argument("--acceptance-output")??Path.Combine(source,"visual"));
            Application.runInBackground=true;
            StartCoroutine(Run());
        }
        IEnumerator Run()
        {
            Directory.CreateDirectory(output);
            world=WorldSnapshot.Parser.ParseFrom(File.ReadAllBytes(Path.Combine(source,"world.bin")));
            var files=Directory.GetFiles(source,"city*.bin");
            cities=files.Select(path=>CitySnapshot.Parser.ParseFrom(File.ReadAllBytes(path))).GroupBy(city=>city.CityId).Select(group=>group.First()).ToArray();
            store.Update(store.State.With(world:world,city:cities[0],selectedCityId:cities[0].CityId,
                session:new SessionInfo{Username="OFFLINE SNAPSHOT REPLAY",Role="player"},connection:ConnectionState.Offline));
            foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(2560,1440)})
            {
                Screen.SetResolution(size.x,size.y,false);
                for(var frame=0;frame<5;frame++)yield return null;
                foreach(var style in new[]{MapVisualStyle.Relief,MapVisualStyle.Manuscript})
                {
                    globe.SetVisualStyle(style);store.Update(store.State.With(visualStyle:style));
                    foreach(var view in new[]{("global",18000000d),("regional",150000d),("city",1800d)})
                    {
                        Focus(0,view.Item2);
                        for(var frame=0;frame<8;frame++)yield return null;
                        yield return new WaitForEndOfFrame();
                        var texture=ScreenCapture.CaptureScreenshotAsTexture();
                        File.WriteAllBytes(Path.Combine(output,$"{size.x}x{size.y}-{style}-{view.Item1}.png"),texture.EncodeToPNG());
                        Destroy(texture);
                    }
                }
            }
            var seconds=double.TryParse(Argument("--acceptance-seconds"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var requested)?Math.Max(1,requested):300;
            var start=Time.realtimeSinceStartupAsDouble;
            var previous=start;
            var nextSample=0d;
            var nextView=0d;
            var step=0;
            using(var drawCalls=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count"))
            {
                while(Time.realtimeSinceStartupAsDouble-start<seconds)
                {
                    yield return null;
                    var now=Time.realtimeSinceStartupAsDouble;
                    frames.Add((now-previous)*1000);previous=now;
                    var elapsed=now-start;
                    if(elapsed>=nextView) { Focus(step%cities.Length,step%3==0?18000000:step%3==1?150000:1800);step++;nextView+=10; }
                    if(elapsed>=nextSample)
                    {
                        samples.Add(new Sample { seconds=elapsed,memory=Profiler.GetTotalAllocatedMemoryLong(),objects=Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Length,drawCalls=drawCalls.Valid?drawCalls.LastValue:0 });
                        nextSample+=5;
                    }
                }
            }
            frames.Sort();
            var report=new Report { unity=Application.unityVersion,gpu=SystemInfo.graphicsDeviceName,seconds=Time.realtimeSinceStartupAsDouble-start,
                p50FrameMs=frames[(int)(frames.Count*.5)],p95FrameMs=frames[Math.Min(frames.Count-1,(int)(frames.Count*.95))],samples=samples.ToArray() };
            File.WriteAllText(Path.Combine(output,"soak.json"),JsonUtility.ToJson(report,true));
            Debug.Log("[Orbis] Visual acceptance replay complete: "+output);
            Application.Quit(0);
        }
        void Focus(int index,double altitude)
        {
            var city=cities[index];
            var location=world.Cities.First(value=>value.Id==city.CityId);
            store.Update(store.State.With(city:city,selectedCityId:city.CityId));
            store.Focus(new GeoCoordinate(location.Pos.Lat,location.Pos.Lon),altitude);
        }
    }
}
