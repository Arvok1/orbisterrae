using System;
using System.Collections.Generic;
using System.Linq;
using Orbis.Core;
using Orbis.Network;
using Orbis.V1;
using UnityEngine;
using UnityEngine.UIElements;

namespace Orbis.UI
{
    public sealed class CityGameplayController : IDisposable
    {
        readonly GameStore store;
        readonly GameNetworkClient network;
        readonly VisualElement panel;
        readonly Label summary=new();
        readonly Label toolStatus=new();
        readonly TextField name=new();
        readonly ScrollView tools=new();
        readonly ScrollView orders=new();
        readonly Button move;
        readonly Button found;
        readonly IVisualElementScheduledItem timer;
        string renderedKey;
        double worldAt;
        double cityAt;
        WorldSnapshot worldClock;
        CitySnapshot cityClock;
        readonly Dictionary<string,Button> orderButtons=new();
        public CityGameplayController(VisualElement root,GameStore gameStore,GameNetworkClient client)
        {
            store=gameStore;network=client;
            panel=new VisualElement { name="gameplay-panel" };
            panel.AddToClassList("side-panel");panel.AddToClassList("left-panel");panel.AddToClassList("input-surface");
            panel.RegisterCallback<PointerEnterEvent>(_=>Orbis.World.HudInputGate.Enter(panel));
            panel.RegisterCallback<PointerLeaveEvent>(_=>Orbis.World.HudInputGate.Leave(panel));
            name.RegisterCallback<FocusInEvent>(_=>Orbis.World.HudInputGate.SetKeyboardFocus(true));
            name.RegisterCallback<FocusOutEvent>(_=>Orbis.World.HudInputGate.SetKeyboardFocus(false));
            panel.Add(summary);panel.Add(name);
            move=new Button(()=> { if(store.State.Selection.HasValue)network.MoveRuler(store.State.Selection.Value); });
            found=new Button(()=> { if(store.State.Selection.HasValue)network.FoundCity(name.value,store.State.Selection.Value); });
            panel.Add(move);panel.Add(found);panel.Add(toolStatus);panel.Add(tools);panel.Add(orders);
            panel.style.maxHeight=600;tools.style.maxHeight=260;orders.style.maxHeight=140;
            root.Add(panel);store.Changed+=Render;
            timer=panel.schedule.Execute(()=>Render(store.State)).Every(250);
            Render(store.State);
        }
        string Text(string key)=>ClientText.Get(store.State.Locale,key);
        void Tool(string key,string kind,string value)
        {
            tools.Add(new Button(()=>store.Update(store.State.With(tool:new CityTool(kind,value,store.State.Tool.Orientation)))) { text=Text(key) });
        }
        void Render(ClientViewState state)
        {
            panel.style.display=state.Session!=null&&!state.Session.IsAdmin?DisplayStyle.Flex:DisplayStyle.None;
            name.label=Text("city_name");move.text=Text("move");found.text=Text("found");
            var ready=state.Connection==ConnectionState.Online&&state.Selection.HasValue&&string.IsNullOrEmpty(state.PendingOperation);
            move.SetEnabled(ready);found.SetEnabled(ready&&!string.IsNullOrWhiteSpace(name.value));
            if(!ReferenceEquals(worldClock,state.World)) { worldClock=state.World;worldAt=Time.realtimeSinceStartupAsDouble; }
            if(!ReferenceEquals(cityClock,state.City)) { cityClock=state.City;cityAt=Time.realtimeSinceStartupAsDouble; }
            var ruler=state.World?.Ruler;
            var now=(state.World?.NowMs??0)+(Time.realtimeSinceStartupAsDouble-worldAt)*1000;
            summary.text=ruler==null?Text("waiting_world"):$"{Text("supplies")}: {ruler.Supplies}\n{Text("journey")}: {ClientText.Remaining(ruler.JourneyDueMs,now)} · {Text("foundation")}: {ClientText.Remaining(ruler.FoundingUntilMs,now)}\n"
                +(state.Selection.HasValue?$"{state.Selection.Value.LatitudeDegrees:F4}, {state.Selection.Value.LongitudeDegrees:F4}":Text("select_location"));
            var preview=state.Preview;
            toolStatus.text=$"{Text("tool")}: {Text(state.Tool.Kind=="building"||state.Tool.Kind=="zone"?state.Tool.Value:state.Tool.Kind)} · {state.Tool.Orientation*90}°\n"
                +(preview==null?Text("server_confirmation"):$"({preview.X}, {preview.Y}) · {preview.Width}×{preview.Height}\n{Text(preview.Reason)}");
            tools.SetEnabled(CityPresentation.CanEdit(state));orders.SetEnabled(CityPresentation.CanEdit(state));
            var city=state.City;
            if(city==null) { tools.Clear();orders.Clear();orderButtons.Clear();renderedKey=null;return; }
            var key=$"{city.CityId}:{city.Revision}:{state.Locale}";
            if(renderedKey!=key)
            {
                renderedKey=key;tools.Clear();
                Tool("inspect","select","");Tool("road","road","road");
                foreach(var zone in new[]{"residential","agricultural","industrial","civic","military"})Tool(zone,"zone",zone);
                foreach(var definition in city.Definitions)Tool(definition.Id,"building",definition.Id);
                tools.Add(new Button(()=>store.Update(store.State.With(tool:new CityTool(store.State.Tool.Kind,store.State.Tool.Value,store.State.Tool.Orientation+1)))) { text=Text("rotate") });
            }
            var retained=new HashSet<string>();
            var cityNow=city.NowMs+(Time.realtimeSinceStartupAsDouble-cityAt)*1000;
            foreach(var order in city.Construction)
            {
                if(order.Status!="active"&&order.Status!="queued")continue;
                var id=order.Id;
                retained.Add(id);
                if(!orderButtons.TryGetValue(id,out var button))
                { button=new Button(()=>network.CancelConstruction(id));orderButtons.Add(id,button);orders.Add(button); }
                button.text=$"{Text(order.BuildingId)} · {Text(order.Status)} · {ClientText.Remaining(order.DueAtMs,cityNow)} · {Text("cancel")}";
            }
            foreach(var id in orderButtons.Keys.Where(id=>!retained.Contains(id)).ToArray())
            { orderButtons[id].RemoveFromHierarchy();orderButtons.Remove(id); }
        }
        public void Dispose()
        {
            timer.Pause();store.Changed-=Render;Orbis.World.HudInputGate.Leave(panel);panel.RemoveFromHierarchy();
        }
    }
}
