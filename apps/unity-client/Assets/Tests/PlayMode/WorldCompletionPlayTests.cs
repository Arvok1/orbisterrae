using System.Collections;
using NUnit.Framework;
using Orbis.Core;
using Orbis.Network;
using Orbis.UI;
using Orbis.V1;
using Orbis.World;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Orbis.Tests.PlayMode
{
    public sealed class WorldCompletionPlayTests
    {
        [Test]
        public void NestedHudSurfacesAndKeyboardFocusDoNotLeakCameraInput()
        {
            var parent=new object();var child=new object();HudInputGate.Reset();
            HudInputGate.Enter(parent);HudInputGate.Enter(child);HudInputGate.Leave(child);
            Assert.That(HudInputGate.IsPointerBlocked,Is.True);HudInputGate.Leave(parent);
            Assert.That(HudInputGate.IsPointerBlocked,Is.False);
            HudInputGate.SetKeyboardFocus(true);Assert.That(HudInputGate.IsKeyboardBlocked,Is.True);HudInputGate.Reset();
        }
        [UnityTest]
        public IEnumerator LocaleSwitchUpdatesExistingCityToolsWithoutNewRevision()
        {
            var city=new CitySnapshot { CityId="city",Revision=5,Editable=true,Width=256,Height=256 };
            city.Definitions.Add(new BuildingDefinition{Id="house",Width=2,Height=2});
            var store=new GameStore(new ClientViewState("pt-BR",MapVisualStyle.Relief,new GeoCoordinate(0,0),1000,"",ConnectionState.Online,
                new SessionInfo(),city:city,selectedCityId:"city"));
            var root=new VisualElement();var host=new GameObject("locale-test");var network=host.AddComponent<GameNetworkClient>();
            using(var controller=new CityGameplayController(root,store,network))
            {
                Assert.That(root.Query<Button>().ToList().Exists(button=>button.text=="Casa"),Is.True);
                store.Update(store.State.With(locale:"en"));
                Assert.That(root.Query<Button>().ToList().Exists(button=>button.text=="House"),Is.True);
                Assert.That(store.State.City.Revision,Is.EqualTo(5));
            }
            Object.Destroy(host);yield return null;
        }
        [UnityTest]
        public IEnumerator EquivalentSnapshotsKeepCityMarkersStable()
        {
            var state=new WorldSnapshot();state.Cities.Add(new CityState{Id="city",Pos=new LatLon{Lat=0,Lon=0},Active=true});
            var store=new GameStore(new ClientViewState("en",MapVisualStyle.Relief,new GeoCoordinate(0,0),1000000,"",world:state));
            var host=new GameObject("cartography-test");var terrain=host.AddComponent<GlobeRenderer>();
            terrain.Initialize(ClientRuntimeConfig.CreateDefaults(Application.temporaryCachePath));
            var map=host.AddComponent<WorldCartography>();map.Initialize(store,terrain);
            yield return null;
            var firstCount=host.transform.childCount;
            store.Update(store.State.With(world:state.Clone()));yield return null;
            Assert.That(map.CityVisualCount,Is.EqualTo(1));Assert.That(host.transform.childCount,Is.EqualTo(firstCount));
            Object.Destroy(host);yield return null;
        }
    }
}
