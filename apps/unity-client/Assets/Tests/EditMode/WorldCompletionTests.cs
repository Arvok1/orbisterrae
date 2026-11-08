using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Orbis.Core;
using Orbis.Network;
using Orbis.V1;
using Orbis.World;
using UnityEngine;

namespace Orbis.Tests.EditMode
{
    public sealed class WorldCompletionTests
    {
        [TestCase(0,0,.5f,.5f)]
        [TestCase(0,-180,0,.5f)]
        [TestCase(85.05112878,90,.75f,1)]
        public void MercatorProjectionMatchesAssets(double latitude,double longitude,float expectedX,float expectedY)
        {
            var uv=GeoPresentation.MercatorUv(new GeoCoordinate(latitude,longitude));
            Assert.That(uv.x,Is.EqualTo(expectedX).Within(.00001));Assert.That(uv.y,Is.EqualTo(expectedY).Within(.00001));
        }
        [Test]
        public void SharedTerritoryEdgeIsRemoved()
        {
            var first=new TerritoryCell { CityId="city" };first.Boundary.Add(new[]{new LatLon{Lat=0,Lon=0},new LatLon{Lat=0,Lon=1},new LatLon{Lat=1,Lon=0}});
            var second=new TerritoryCell { CityId="city" };second.Boundary.Add(new[]{new LatLon{Lat=0,Lon=1},new LatLon{Lat=0,Lon=0},new LatLon{Lat=-1,Lon=0}});
            Assert.That(GeoPresentation.TerritoryEdges(new[]{first,second})["city"].Count,Is.EqualTo(4));
        }
        [Test]
        public void ConcaveLakeTriangulatesWithoutFanOutsidePolygon()
        {
            var points=new[]{new Vector2(0,0),new Vector2(3,0),new Vector2(3,3),new Vector2(1.5f,1),new Vector2(0,3)};
            Assert.That(PolygonTriangulator.Triangulate(points).Length,Is.EqualTo(9));
        }
        [Test]
        public void ResponseChecksGenerationSequenceRequestAndCityRevision()
        {
            var receipt=new CommandReceipt(7,3,"request","city",9,0);
            var envelope=new Envelope { Seq=7,Server=new ServerMsg { Result=new CommandResult { RequestId="request",Ok=true } } };
            Assert.That(receipt.Accept(envelope,3),Is.True);Assert.That(receipt.Accept(envelope,2),Is.False);
            envelope.Server.Result.RequestId="other";Assert.That(receipt.Accept(envelope,3),Is.False);
            envelope.Server=new ServerMsg{City=new CitySnapshot{CityId="city",Revision=8}};Assert.That(receipt.Accept(envelope,3),Is.False);
            envelope.Server.City.Revision=9;Assert.That(receipt.Accept(envelope,3),Is.True);
            envelope.Server.City.CityId="foreign";Assert.That(receipt.Accept(envelope,3),Is.False);
        }
        [Test]
        public void EveryUrbanRoleHasDeterministicGeometryAndThreeLods()
        {
            Assert.That(UrbanKit.Roles.Length,Is.EqualTo(15));
            foreach(var role in UrbanKit.Roles)
                for(var lod=0;lod<3;lod++)
                {
                    var first=UrbanKit.Build(role,1,lod);var second=UrbanKit.Build(role,1,lod);
                    Assert.That(first.vertices,Is.EqualTo(second.vertices));Assert.That(first.vertexCount,Is.GreaterThan(0));
                    Assert.That(first.subMeshCount,Is.EqualTo(4));
                    UnityEngine.Object.DestroyImmediate(first);UnityEngine.Object.DestroyImmediate(second);
                }
        }
        [Test]
        public void CityStatCardsExposePopulationMetricsAndEvents()
        {
            var city=new CitySnapshot { Name="Paris",Revision=3,Editable=true,
                Metrics=new CityMetrics{Water=.836,Density=.421,Defense=7.25},
                Population=new PopulationState{Farmers=10,Artisans=4,Soldiers=2,Priests=1} };
            city.Events.Add(new CityEventState { Kind="famine" });
            city.Events.Add(new CityEventState { Kind="fire" });
            Assert.That(CityPresentation.Population(city),Is.EqualTo(17));
            var metrics=CityPresentation.MetricsText(city,"en");
            Assert.That(metrics,Does.Contain("84%"));Assert.That(metrics,Does.Contain("42%"));
            Assert.That(metrics,Does.Contain("7.3"));Assert.That(metrics,Does.Contain("population: 17"));
            var classes=CityPresentation.ClassesText(city,"en");
            Assert.That(classes,Does.Contain("Farmers: 10"));Assert.That(classes,Does.Contain("Priests: 1"));
            Assert.That(CityPresentation.EventsText(city,"en"),Is.EqualTo("⚡ famine · 🔥 fire"));
            Assert.That(CityPresentation.EventsText(city,"pt"),Is.EqualTo("⚡ fome · 🔥 incêndio"));
            var empty=new CitySnapshot();
            Assert.That(CityPresentation.MetricsText(empty,"en"),Is.Empty);
            Assert.That(CityPresentation.ClassesText(empty,"en"),Is.Empty);
            Assert.That(CityPresentation.EventsText(empty,"en"),Is.Empty);
        }
        [Test]
        public void PreviewRejectsOccupiedBoundsAndReadOnlyWithoutChangingSnapshot()
        {
            var city=new CitySnapshot { CityId="city",Width=256,Height=256,Editable=true };
            city.Definitions.Add(new BuildingDefinition { Id="house",Width=2,Height=3 });
            city.Buildings.Add(new BuildingState { Id="house1",BuildingId="house",X=10,Y=10 });
            var state=new ClientViewState("en",MapVisualStyle.Relief,new GeoCoordinate(0,0),1000,"",ConnectionState.Online,
                city:city,selectedCityId:"city",tool:new CityTool("building","house"));
            Assert.That(PlacementPreview.Evaluate(state,10,10).Reason,Is.EqualTo("occupied"));
            Assert.That(PlacementPreview.Evaluate(state,255,255).Valid,Is.False);
            Assert.That(PlacementPreview.Evaluate(state,20,20).Valid,Is.True);
            Assert.That(PlacementPreview.Evaluate(state.With(connection:ConnectionState.Offline),20,20).Valid,Is.False);
            Assert.That(city.Buildings.Count,Is.EqualTo(1));
        }
        [Test]
        public void PackagePathsAndHashesFailClosed()
        {
            var root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root,"manifest.json"),"{\"format\":\"orbis-world-v2\",\"version\":2,\"tile_side\":65,\"max_lod\":0}");
                var package=new WorldPackage(root);
                Assert.Throws<InvalidDataException>(()=>package.ResolveContained("../escape"));
                File.WriteAllText(Path.Combine(root,"data.bin"),"hello");
                Assert.Throws<InvalidDataException>(()=>package.ResolveVisual(new WorldFileDescriptor{path="data.bin",bytes=5,sha256="bad"}));
                Assert.Throws<InvalidDataException>(()=>package.ResolveVisual(new WorldFileDescriptor{path="data.bin",bytes=6,sha256="bad"}));
            }
            finally { Directory.Delete(root,true); }
        }
        [Test]
        public void LocaleChangesIndependentOfCityRevision()
        {
            Assert.That(ClientText.Get("pt-BR","house"),Is.EqualTo("Casa"));Assert.That(ClientText.Get("en","house"),Is.EqualTo("House"));
            Assert.That(ClientText.Remaining(120000,30000),Is.EqualTo("01:30"));Assert.That(ClientText.Remaining(1000,2000),Is.EqualTo("00:00"));
        }
        [Test]
        public void OverlayKeepsCachedLevelsAliveAcrossZoomSwitches()
        {
            var store=new GameStore(new ClientViewState("en",MapVisualStyle.Relief,new GeoCoordinate(0,0),3000000,""));
            var host=new GameObject("overlay-cache-test");
            try
            {
                var terrain=host.AddComponent<GlobeRenderer>();
                terrain.Initialize(ClientRuntimeConfig.CreateDefaults(Application.temporaryCachePath));
                Field(terrain,"<HydrographyPath>k__BackingField",string.Empty);
                var overlay=host.AddComponent<GeographicOverlay>();
                overlay.Initialize(store,terrain);
                LateUpdate(overlay);
                var levels=(System.Collections.IDictionary)Field(overlay,"levels");
                var orbital=Field(overlay,"current");
                Assert.That(orbital,Is.Not.Null);Assert.That(levels.Count,Is.EqualTo(1));
                store.Update(store.State.With(altitudeMeters:300000));
                LateUpdate(overlay);
                var mid=Field(overlay,"current");
                store.Update(store.State.With(altitudeMeters:30000));
                LateUpdate(overlay);
                Assert.That(levels.Count,Is.EqualTo(3));Assert.That(Field(overlay,"current"),Is.Not.SameAs(mid));
                store.Update(store.State.With(altitudeMeters:3000000));
                LateUpdate(overlay);
                Assert.That(Field(overlay,"current"),Is.SameAs(orbital));Assert.That(levels.Count,Is.EqualTo(3));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
        static object Field(object target,string name)
        {
            var type=target.GetType();
            while(type!=null)
            {
                var field=type.GetField(name,BindingFlags.NonPublic|BindingFlags.Instance);
                if(field!=null)return field.GetValue(target);
                type=type.BaseType;
            }
            throw new MissingFieldException(target.GetType().Name,name);
        }
        static void Field(object target,string name,object value)
        {
            var type=target.GetType();
            while(type!=null)
            {
                var field=type.GetField(name,BindingFlags.NonPublic|BindingFlags.Instance);
                if(field!=null) { field.SetValue(target,value);return; }
                type=type.BaseType;
            }
        }
        static void LateUpdate(object behaviour)=>behaviour.GetType().GetMethod("LateUpdate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(behaviour,null);
    }
}
