using NUnit.Framework;
using Orbis.Network;
using Orbis.V1;

namespace Orbis.Tests.EditMode
{
    public sealed class AdminSnapshotMergeTests
    {
        [Test]
        public void IncrementalSnapshotKeepsWorldAndDeduplicatesBoundedEvents()
        {
            var current = new AdminSnapshot { NextEventSeq = 2 };
            current.Cities.Add(new CityState { Id = "city" });
            current.Entities.Add(new EntityUpsert { Id = "ruler" });
            current.Events.Add(new AdminEvent { Seq = 1, Kind = 1 });
            current.Events.Add(new AdminEvent { Seq = 2, Kind = 2 });
            var incoming = new AdminSnapshot { NextEventSeq = 3 };
            incoming.Events.Add(new AdminEvent { Seq = 2, Kind = 20 });
            incoming.Events.Add(new AdminEvent { Seq = 3, Kind = 3 });

            var merged = AdminSnapshotMerge.Apply(current, incoming, 2);

            Assert.That(merged.Cities.Count, Is.EqualTo(1));
            Assert.That(merged.Entities.Count, Is.EqualTo(1));
            Assert.That(merged.Events.Count, Is.EqualTo(2));
            Assert.That(merged.Events[0].Seq, Is.EqualTo(2));
            Assert.That(merged.Events[0].Kind, Is.EqualTo(20));
            Assert.That(merged.Events[1].Seq, Is.EqualTo(3));
        }
    }
}
