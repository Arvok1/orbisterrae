//! Deterministic in-memory kernels measured by P0.6. Persistence belongs to S1.1.
use std::cmp::Reverse;
use std::collections::{BTreeMap, BinaryHeap, HashSet};
use std::hash::{BuildHasherDefault, DefaultHasher};

#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct EventId(pub u64);

#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub struct ScheduledEvent {
    pub due_at_ms: u64,
    pub id: EventId,
}

#[derive(Default)]
pub struct EventQueue {
    heap: BinaryHeap<Reverse<ScheduledEvent>>,
    // Only membership is observable; ordering is exclusively the heap's (due, ID).
    // Fixed hasher avoids hidden entropy in the deterministic simulation kernel.
    seen: HashSet<EventId, BuildHasherDefault<DefaultHasher>>,
}

impl EventQueue {
    pub fn schedule(&mut self, event: ScheduledEvent) -> bool {
        if !self.seen.insert(event.id) {
            return false;
        }
        self.heap.push(Reverse(event));
        true
    }

    /// The caller supplies time; no wall clock or I/O in the kernel.
    pub fn drain_due(&mut self, now_ms: u64, limit: usize) -> Vec<ScheduledEvent> {
        let mut result = Vec::new();
        while result.len() < limit {
            let Some(Reverse(event)) = self.heap.peek() else {
                break;
            };
            if event.due_at_ms > now_ms {
                break;
            }
            let Reverse(event) = self.heap.pop().expect("peeked event");
            result.push(event);
        }
        result
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub struct CityId(pub u64);

#[derive(Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord)]
pub struct CellId(pub u64);

#[derive(Clone, Copy, Debug)]
pub struct Claim {
    pub cell: CellId,
    pub city: CityId,
    pub influence: f64,
}

/// Resolve already-computed local candidates; ties always use the lower city ID.
/// H3 neighbourhood generation and geographic distance are outside this benchmark.
pub fn assign_cells(claims: &[Claim], minimum: f64) -> BTreeMap<CellId, CityId> {
    let mut winners: BTreeMap<CellId, (CityId, f64)> = BTreeMap::new();
    for claim in claims {
        if !claim.influence.is_finite() || claim.influence < minimum {
            continue;
        }
        let winner = winners
            .entry(claim.cell)
            .or_insert((claim.city, claim.influence));
        if claim.influence > winner.1 || (claim.influence == winner.1 && claim.city < winner.0) {
            *winner = (claim.city, claim.influence);
        }
    }
    winners
        .into_iter()
        .map(|(cell, (city, _))| (cell, city))
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn queue_orders_equal_deadlines_and_never_replays_ids() {
        let mut queue = EventQueue::default();
        for id in [3, 1, 2] {
            assert!(queue.schedule(ScheduledEvent {
                due_at_ms: 10,
                id: EventId(id)
            }));
        }
        assert!(queue.drain_due(9, 10).is_empty());
        assert_eq!(
            queue
                .drain_due(10, 2)
                .iter()
                .map(|e| e.id.0)
                .collect::<Vec<_>>(),
            [1, 2]
        );
        assert!(!queue.schedule(ScheduledEvent {
            due_at_ms: 20,
            id: EventId(1)
        }));
        assert!(!queue.schedule(ScheduledEvent {
            due_at_ms: 20,
            id: EventId(3)
        }));
        assert_eq!(queue.drain_due(10, 10)[0].id, EventId(3));
    }

    #[test]
    fn ownership_is_unique_and_independent_of_candidate_order() {
        let mut claims = vec![
            Claim {
                cell: CellId(1),
                city: CityId(3),
                influence: 2.0,
            },
            Claim {
                cell: CellId(1),
                city: CityId(2),
                influence: 2.0,
            },
            Claim {
                cell: CellId(2),
                city: CityId(4),
                influence: 0.01,
            },
            Claim {
                cell: CellId(3),
                city: CityId(5),
                influence: f64::NAN,
            },
        ];
        let expected = assign_cells(&claims, 0.05);
        claims.reverse();
        assert_eq!(expected, assign_cells(&claims, 0.05));
        assert_eq!(expected, BTreeMap::from([(CellId(1), CityId(2))]));
    }

    #[test]
    fn repeated_workloads_produce_identical_event_order() {
        fn run() -> Vec<ScheduledEvent> {
            let mut queue = EventQueue::default();
            for id in 0..10_000 {
                queue.schedule(ScheduledEvent {
                    due_at_ms: (id * 7919 + 42) % 1000,
                    id: EventId(id),
                });
            }
            queue.drain_due(1000, 10_000)
        }
        assert_eq!(run(), run());
    }
}
