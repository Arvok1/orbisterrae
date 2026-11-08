using System;
using System.Collections.Generic;
using Orbis.V1;

namespace Orbis.Core
{
    /// <summary>
    /// Economy read model (S1.3b): hub roster, the selected hub with its live
    /// market, and the player's caravans. Plain C# state without network or
    /// Unity dependencies, so EditMode tests can drive it directly. The network
    /// client applies protocol messages; the globe and the trade HUD consume
    /// it. Clients stay render-only: every change mirrors an authoritative
    /// server message and nothing is inferred locally.
    /// </summary>
    public sealed class EconomyStore
    {
        readonly List<HubState> hubs = new();
        readonly List<CaravanState> caravans = new();

        /// <summary>Full hub roster in gateway order (name-sorted per query).</summary>
        public IReadOnlyList<HubState> Hubs => hubs;
        /// <summary>Caravans seen by the session, upserted by id.</summary>
        public IReadOnlyList<CaravanState> Caravans => caravans;
        /// <summary>Currently open trading post; empty when none is selected.</summary>
        public string SelectedHubId { get; private set; } = string.Empty;
        /// <summary>Fresh hub detail for the selected hub; null until it arrives.</summary>
        public HubState Hub { get; private set; }
        /// <summary>Market snapshot for the selected hub; null until it arrives.</summary>
        public MarketSnapshot Market { get; private set; }

        public event Action Changed;

        /// <summary>Replaces the entire roster received from <c>listHubs</c>.</summary>
        public void ApplyHubs(HubsState state)
        {
            hubs.Clear();
            if (state != null) hubs.AddRange(state.Hubs);
            RaiseChanged();
        }

        /// <summary>Applies a hub detail reply, but only for the selected hub: late replies for a hub the player already left must not overwrite the open one.</summary>
        public void ApplyHub(HubState hub)
        {
            if (hub == null || hub.Id != SelectedHubId) return;
            Hub = hub;
            RaiseChanged();
        }

        /// <summary>Applies a market snapshot for the selected hub only.</summary>
        public void ApplyMarket(MarketSnapshot snapshot)
        {
            if (snapshot == null || snapshot.HubId != SelectedHubId) return;
            Market = snapshot;
            RaiseChanged();
        }

        /// <summary>Upserts a caravan by id: latest revision wins.</summary>
        public void ApplyCaravan(CaravanState caravan)
        {
            if (string.IsNullOrEmpty(caravan?.Id)) return;
            for (var index = 0; index < caravans.Count; index++)
            {
                if (caravans[index].Id != caravan.Id) continue;
                if (caravan.Revision >= caravans[index].Revision) caravans[index] = caravan;
                RaiseChanged();
                return;
            }
            caravans.Add(caravan);
            RaiseChanged();
        }

        /// <summary>Opens a trading post (or closes it with an empty id) and drops stale detail until fresh replies arrive.</summary>
        public void SelectHub(string hubId)
        {
            var next = hubId ?? string.Empty;
            if (next == SelectedHubId) return;
            SelectedHubId = next;
            Hub = null;
            Market = null;
            RaiseChanged();
        }

        /// <summary>Looks up a roster hub by id.</summary>
        public HubState FindHub(string hubId)
        {
            foreach (var hub in hubs)
                if (hub.Id == hubId) return hub;
            return null;
        }

        /// <summary>Looks up the first roster hub with the given name (dropdown labels are names).</summary>
        public HubState FindHubByName(string name)
        {
            foreach (var hub in hubs)
                if (hub.Name == name) return hub;
            return null;
        }

        /// <summary>Clears every snapshot: used on logout and on fresh sign-in.</summary>
        public void Clear()
        {
            hubs.Clear();
            caravans.Clear();
            SelectedHubId = string.Empty;
            Hub = null;
            Market = null;
            RaiseChanged();
        }

        void RaiseChanged()=>Changed?.Invoke();
    }
}
