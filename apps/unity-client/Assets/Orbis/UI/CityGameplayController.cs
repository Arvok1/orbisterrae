using System;
using Orbis.Core;
using Orbis.Network;
using UnityEngine.UIElements;

namespace Orbis.UI
{
    public sealed class CityGameplayController : IDisposable
    {
        readonly GameStore store;
        readonly GameNetworkClient network;
        readonly VisualElement panel;
        readonly Label summary = new();
        readonly TextField name = new();
        readonly ScrollView tools = new();
        readonly ScrollView orders = new();
        CitySnapshotKey rendered;
        readonly Button move;
        readonly Button found;
        bool english;

        readonly struct CitySnapshotKey
        {
            public readonly string Id;
            public readonly ulong Revision;
            public CitySnapshotKey(string id, ulong revision) { Id = id; Revision = revision; }
        }

        public CityGameplayController(VisualElement root, GameStore gameStore, GameNetworkClient client)
        {
            store = gameStore;
            network = client;
            panel = new VisualElement { name = "gameplay-panel" };
            panel.AddToClassList("side-panel");
            panel.AddToClassList("left-panel");
            panel.AddToClassList("input-surface");
            panel.RegisterCallback<PointerEnterEvent>(_ => Orbis.World.HudInputGate.Enter());
            panel.RegisterCallback<PointerLeaveEvent>(_ => Orbis.World.HudInputGate.Leave());
            panel.Add(summary);
            panel.Add(name);
            move = new Button(() => { if (store.State.Selection.HasValue) network.MoveRuler(store.State.Selection.Value); });
            found = new Button(() => { if (store.State.Selection.HasValue) network.FoundCity(name.value, store.State.Selection.Value); });
            panel.Add(move);
            panel.Add(found);
            panel.Add(tools);
            panel.Add(orders);
            panel.style.maxHeight = 600;
            tools.style.maxHeight = 280;
            root.Add(panel);
            store.Changed += Render;
            Render(store.State);
        }

        void Tool(string label, string kind, string value)
        {
            tools.Add(new Button(() => store.Update(store.State.With(tool: new CityTool(kind, value, store.State.Tool.Orientation)))) { text = label });
        }

        void Render(ClientViewState state)
        {
            english = state.Locale == "en";
            panel.style.display = state.Session != null && !state.Session.IsAdmin ? DisplayStyle.Flex : DisplayStyle.None;
            name.label = english ? "City name" : "Nome da cidade";
            move.text = english ? "Move to selected location" : "Mover até a seleção";
            found.text = english ? "Found city here" : "Fundar cidade aqui";
            var ready = state.Connection == ConnectionState.Online && state.Selection.HasValue && string.IsNullOrEmpty(state.PendingOperation);
            move.SetEnabled(ready);
            found.SetEnabled(ready);
            var ruler = state.World?.Ruler;
            summary.text = ruler == null ? (english ? "Waiting for world…" : "Aguardando mundo…")
                : $"{(english ? "Supplies" : "Suprimentos")}: {ruler.Supplies}\n"
                    + $"{(english ? "Journey / foundation deadline" : "Prazo de viagem / fundação")}: {ruler.JourneyDueMs:N0} / {ruler.FoundingUntilMs:N0}\n"
                    + (state.Selection.HasValue ? $"{state.Selection.Value.LatitudeDegrees:F4}, {state.Selection.Value.LongitudeDegrees:F4}" : (english ? "Click the globe" : "Clique no globo"));
            tools.SetEnabled(CityPresentation.CanEdit(state));
            orders.SetEnabled(CityPresentation.CanEdit(state));
            var city = state.City;
            if (city == null) { tools.Clear(); orders.Clear(); rendered = default; return; }
            if (rendered.Id == city.CityId && rendered.Revision == city.Revision && tools.childCount > 0) return;
            rendered = new CitySnapshotKey(city.CityId, city.Revision);
            tools.Clear(); orders.Clear();
            Tool(english ? "Inspect" : "Inspecionar", "select", "");
            Tool(english ? "Road" : "Rua", "road", "road");
            foreach (var zone in new[] { "residential", "agricultural", "industrial", "civic", "military" }) Tool(zone, "zone", zone);
            foreach (var definition in city.Definitions) Tool(definition.Id, "building", definition.Id);
            tools.Add(new Button(() => store.Update(store.State.With(tool: new CityTool(store.State.Tool.Kind, store.State.Tool.Value, store.State.Tool.Orientation + 1))))
                { text = english ? "Rotate 90°" : "Girar 90°" });
            foreach (var order in city.Construction)
            {
                if (order.Status != "active" && order.Status != "queued") continue;
                var id = order.Id;
                orders.Add(new Button(() => network.CancelConstruction(id)) { text = $"{order.BuildingId} · {order.Status} · {(english ? "Cancel" : "Cancelar")}" });
            }
        }

        public void Dispose() { store.Changed -= Render; panel.RemoveFromHierarchy(); }
    }
}
