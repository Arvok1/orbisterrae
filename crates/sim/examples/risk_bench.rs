use orbis_protocol::{LatLon, SetViewport};
use orbis_sim::risk::{assign_cells, CellId, CityId, Claim, EventId, EventQueue, ScheduledEvent};
use std::{hint::black_box, time::Instant};

fn percentile(samples: &mut [f64], ratio: f64) -> f64 {
    samples.sort_by(f64::total_cmp);
    samples[((samples.len() as f64 * ratio).ceil() as usize).saturating_sub(1)]
}

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let balance: toml::Value = include_str!("../../../config/balance/territory.toml").parse()?;
    let minimum = balance["influence"]["min_owner_influence"]
        .as_float()
        .ok_or("missing influence threshold")?;
    let started = Instant::now();
    let mut queue = EventQueue::default();
    let mut scheduling = Vec::new();
    let mut completed = 0;
    let mut dispatch = Vec::new();
    for batch in 0..1000 {
        let batch_start = Instant::now();
        for index in 0..1000 {
            let id = batch * 1000 + index;
            assert!(queue.schedule(ScheduledEvent {
                due_at_ms: (id * 7919 + 42) % 1_000_000,
                id: EventId(id)
            }));
        }
        scheduling.push(batch_start.elapsed().as_secs_f64() * 1000.0);
    }
    while completed < 1_000_000 {
        let batch_start = Instant::now();
        completed += black_box(queue.drain_due(1_000_000, 1000)).len();
        dispatch.push(batch_start.elapsed().as_secs_f64() * 1000.0);
    }
    let throughput = 1_000_000.0 / started.elapsed().as_secs_f64();
    let scheduling_p99 = percentile(&mut scheduling, 0.99);
    let dispatch_p99 = percentile(&mut dispatch, 0.99);

    let claims = (0..100_000)
        .map(|id| Claim {
            city: CityId(id),
            cell: CellId(id / 100),
            influence: 1.0 + (id % 97) as f64,
        })
        .collect::<Vec<_>>();
    let mut ownership = Vec::new();
    for _ in 0..100 {
        let started = Instant::now();
        assert_eq!(
            black_box(assign_cells(black_box(&claims), minimum)).len(),
            1000
        );
        ownership.push(started.elapsed().as_secs_f64() * 1000.0);
    }
    let ownership_p99 = percentile(&mut ownership, 0.99);

    let mut view = SetViewport {
        center: Some(LatLon {
            lat: 14.0,
            lon: 8.0,
        }),
        zoom: 3.0,
        width_px: 1280.0,
        height_px: 720.0,
    };
    let mut interest = Vec::new();
    for index in 0..1000 {
        view.zoom = (index % 7) as f64;
        let started = Instant::now();
        black_box(orbis_sim::demo::snapshot(black_box(&view), 10_000)?);
        interest.push(started.elapsed().as_secs_f64() * 1000.0);
    }
    let interest_p99 = percentile(&mut interest, 0.99);
    println!("scheduler_events_per_second={throughput:.0}");
    println!("schedule_batch_1000_p99_ms={scheduling_p99:.3}");
    println!("dispatch_batch_1000_p99_ms={dispatch_p99:.3}");
    println!("ownership_100000_claims_p99_ms={ownership_p99:.3}");
    println!("interest_10000_entities_p99_ms={interest_p99:.3}");
    if throughput < 1_000_000.0
        || scheduling_p99 >= 5.0
        || dispatch_p99 >= 5.0
        || ownership_p99 >= 50.0
        || interest_p99 >= 5.0
    {
        return Err(
            "P0.6 risk budget exceeded; inspect measurements before accepting Rust decision".into(),
        );
    }
    Ok(())
}
