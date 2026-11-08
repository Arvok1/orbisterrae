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
        readonly Label cityHeader=new();
        readonly Label cityStats=new();
        readonly Label toolStatus=new();
        readonly TextField name=new();
        readonly ScrollView tools=new();
        readonly ScrollView orders=new();
        readonly VisualElement tradeGroup=new();
        readonly Label tradeTitle=new();
        readonly ScrollView tradeHubs=new();
        readonly Label tradeHubStats=new();
        readonly ScrollView tradeGoods=new();
        readonly IntegerField tradeQty=new();
        readonly Button tradeDispatch;
        readonly Label tradeCaravans=new();
        readonly Button move;
        readonly Button found;
        readonly IVisualElementScheduledItem timer;
        string renderedKey;
        double worldAt;
        double cityAt;
        string renderedHubKey;
        string renderedGoodsKey;
        string chosenCommodity;
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
            tradeQty.RegisterCallback<FocusInEvent>(_=>Orbis.World.HudInputGate.SetKeyboardFocus(true));
            tradeQty.RegisterCallback<FocusOutEvent>(_=>Orbis.World.HudInputGate.SetKeyboardFocus(false));
            panel.Add(summary);panel.Add(name);panel.Add(cityHeader);panel.Add(cityStats);
            move=new Button(()=> { if(store.State.Selection.HasValue)network.MoveRuler(store.State.Selection.Value); });
            found=new Button(()=> { if(store.State.Selection.HasValue)network.FoundCity(name.value,store.State.Selection.Value); });
            panel.Add(move);panel.Add(found);panel.Add(toolStatus);panel.Add(tools);panel.Add(orders);
            tradeDispatch=new Button(DispatchCaravan);
            tradeHubs.style.maxHeight=120;tradeGoods.style.maxHeight=160;
            tradeGroup.Add(tradeTitle);tradeGroup.Add(tradeHubs);tradeGroup.Add(tradeHubStats);
            tradeGroup.Add(tradeGoods);tradeGroup.Add(tradeQty);tradeGroup.Add(tradeDispatch);tradeGroup.Add(tradeCaravans);
            panel.Add(tradeGroup);
            panel.style.maxHeight=600;tools.style.maxHeight=260;orders.style.maxHeight=140;tradeGroup.style.maxHeight=380;
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
            RenderTrade(state);
            var city=state.City;
            if(city==null) { cityHeader.text=string.Empty;cityStats.text=string.Empty; tools.Clear();orders.Clear();orderButtons.Clear();renderedKey=null;return; }
            cityHeader.text=$"{city.Name} · {Text(city.Editable?"editable":"read_only")} · rev {city.Revision}";
            cityStats.text=string.Join("\n",new[]{CityPresentation.MetricsText(city,state.Locale),CityPresentation.ClassesText(city,state.Locale),CityPresentation.EventsText(city,state.Locale)}.Where(part=>part.Length>0));
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
        /// <summary>Trade HUD (S1.3b): pick a hub, choose a commodity, dispatch a caravan from the ruler's own city.</summary>
        void RenderTrade(ClientViewState state)
        {
            tradeTitle.text=Text("trade");
            tradeQty.label=Text("trade_qty");tradeDispatch.text=Text("trade_dispatch");
            var economy=network.Economy;
            var ruler=state.World?.Ruler;
            var hub=economy.FindHub(economy.SelectedHubId)??economy.Hub;
            var ownCity=state.City!=null&&ruler?.CityId==state.City.CityId;
            var visible=state.Session!=null&&!state.Session.IsAdmin&&economy.Hubs.Count>0&&ruler?.CityId!=null&&ownCity;
            tradeGroup.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
            if(!visible) { tradeHubs.Clear();tradeGoods.Clear();renderedHubKey=null;renderedGoodsKey=null;return; }
            if(economy.SelectedHubId.Length==0&&economy.Hubs.Count>0)network.SelectHub(economy.Hubs[0].Id);
            var hubKey=string.Join(";",economy.Hubs.Select(item=>item.Id))+"@"+economy.SelectedHubId;
            if(hubKey!=renderedHubKey)
            {
                renderedHubKey=hubKey;tradeHubs.Clear();chosenCommodity=string.Empty;
                foreach(var item in economy.Hubs)
                {
                    var captured=item;
                    tradeHubs.Add(new Button(()=> { if(economy.SelectedHubId!=captured.Id)network.SelectHub(captured.Id); })
                        { text=$"{(economy.SelectedHubId==captured.Id?"> ":"")}{captured.Name}" });
                }
            }
            if(hub!=null)
            {
                var stock=string.Join(", ",hub.Stock.Select(entry=>$"{entry.Key} {entry.Value}"));
                if(stock.Length==0)stock=Text("none");
                tradeHubStats.text=$"{Text("trade_fee")}: {ClientText.Percent(hub.Fee)} · {Text("trade_metal")}: {hub.Metal}\n{Text("trade_stock")}: {stock}";
            }
            else tradeHubStats.text=Text("loading");
            if(hub!=null&&chosenCommodity.Length==0&&hub.Stock.Count>0)chosenCommodity=hub.Stock.Keys.First();
            var marketLine=string.Empty;
            var snapshot=economy.Market;
            if(snapshot!=null&&snapshot.HubId==hub?.Id&&snapshot.Commodity==chosenCommodity&&chosenCommodity.Length>0)
                marketLine=$"\n{chosenCommodity}: {snapshot.LocalPrice:F2}";
            tradeHubStats.text+=marketLine;
            var goodsKey=economy.SelectedHubId+"@"+(hub!=null?string.Join(";",hub.Stock.Keys):string.Empty)+"|"+chosenCommodity;
            if(goodsKey!=renderedGoodsKey&&hub!=null)
            {
                renderedGoodsKey=goodsKey;tradeGoods.Clear();
                foreach(var commodity in hub.Stock.Keys)
                {
                    var captured=commodity;
                    tradeGoods.Add(new Button(()=> { if(chosenCommodity!=captured){chosenCommodity=captured;network.RequestMarket(hub.Id,captured);} })
                        { text=$"{(chosenCommodity==captured?"> ":"")}{captured}" });
                }
            }
            var quantity=Math.Clamp(tradeQty.value,0,1_000_000);
            tradeDispatch.SetEnabled(state.Connection==ConnectionState.Online&&hub!=null&&chosenCommodity.Length>0
                &&quantity>0&&string.IsNullOrEmpty(state.PendingOperation));
            var status=new List<string>();
            foreach(var caravan in economy.Caravans.Skip(Math.Max(0,economy.Caravans.Count-4)))
            {
                var cargo=string.Join(", ",caravan.Cargo.Select(entry=>$"{entry.Key} {entry.Value}"));
                status.Add($"{Text(caravan.Status)} · {cargo} · {ClientText.Remaining(caravan.DueAtMs,state.World?.NowMs??0)}");
            }
            tradeCaravans.text=status.Count>0?string.Join("\n",status):string.Empty;
        }
        void DispatchCaravan()
        {
            var state=store.State;
            var ruler=state.World?.Ruler;
            if(ruler?.CityId==null||chosenCommodity.Length==0) return;
            var quantity=(uint)Math.Clamp(tradeQty.value,0,1_000_000);
            network.DispatchCaravan(ruler.CityId,network.Economy.SelectedHubId,chosenCommodity,quantity,0);
        }
        public void Dispose()
        {
            timer.Pause();store.Changed-=Render;Orbis.World.HudInputGate.Leave(panel);panel.RemoveFromHierarchy();
        }
    }
}
