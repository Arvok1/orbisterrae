using System.Collections.Generic;
using System.Linq;
using Orbis.V1;

namespace Orbis.Network
{
    public static class AdminSnapshotMerge
    {
        public static AdminSnapshot Apply(AdminSnapshot current, AdminSnapshot incoming, int maximumEvents = 200)
        {
            var merged = incoming.Clone();
            if (merged.Cities.Count == 0 && current != null) merged.Cities.Add(current.Cities);
            if (merged.Entities.Count == 0 && current != null) merged.Entities.Add(current.Entities);
            if (merged.Territory.Count == 0 && current != null) merged.Territory.Add(current.Territory);
            if (current == null || incoming.Events.Count == 0) return merged;

            var ordered = current.Events.Concat(incoming.Events)
                .GroupBy(item => item.Seq)
                .Select(group => group.Last())
                .OrderBy(item => item.Seq)
                .ToList();
            var first = System.Math.Max(0, ordered.Count - System.Math.Max(1, maximumEvents));
            merged.Events.Clear();
            for (var index = first; index < ordered.Count; index++) merged.Events.Add(ordered[index]);
            return merged;
        }
    }
}
