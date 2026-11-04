//! Test fixture generation, not gameplay. All sites are revalidated by commands.
use h3o::{LatLng, Resolution};
use orbis_core::world::Position;
use orbis_sim::world::{self, Balance, Geography};

fn main() -> Result<(), Box<dyn std::error::Error>> {
    let geo = Geography::from_json(
        include_str!("../../../apps/client/src/assets/world-land-v1.json"),
        include_str!("../../../apps/client/src/assets/world-terrain-v1.json"),
    )?;
    let balance: Balance =
        toml::from_str(include_str!("../../../config/balance/first_village.toml"))?;
    let mut sites: Vec<Position> = Vec::new();
    for index in 0..11700 {
        let index = (index * 7919 + 42) % 11700;
        let pos = Position {
            lat: -55.0 + (index / 180) as f64 * 2.0,
            lon: -179.0 + (index % 180) as f64 * 2.0,
        };
        if geo.is_land(pos)
            && geo.near_water(pos, balance.village.water_distance_m)
            && sites
                .iter()
                .all(|site| site.distance_m(pos) >= balance.village.minimum_distance_m)
        {
            sites.push(pos);
        }
        if sites.len() == 100 {
            break;
        }
    }
    assert_eq!(sites.len(), 100);
    let cell = LatLng::new(balance.spawn.lat, balance.spawn.lon)?.to_cell(Resolution::Three);
    let mut crossing = None;
    'vertices: for vertex in cell.boundary().iter() {
        for (dx, dy) in [(0.001, 0.0), (0.0, 0.001), (0.001, 0.001)] {
            let from = Position {
                lat: vertex.lat() + dy,
                lon: vertex.lng() + dx,
            };
            let to = Position {
                lat: vertex.lat() - dy,
                lon: vertex.lng() - dx,
            };
            if world::worker(world::shard(from)) != world::worker(world::shard(to))
                && geo.route_is_land(from, to, balance.ruler.path_sample_m)
                && geo.near_water(to, balance.village.water_distance_m)
            {
                crossing = Some(
                    serde_json::json!({"from":from,"to":to,"sourceWorker":world::worker(world::shard(from)),"destinationWorker":world::worker(world::shard(to))}),
                );
                break 'vertices;
            }
        }
    }
    let fixture = serde_json::json!({"seed":42,"sites":sites,"crossing":crossing.ok_or("no suitable worker boundary")?});
    let path = std::env::args()
        .nth(1)
        .unwrap_or_else(|| "scenarios/first_village_sites.json".into());
    std::fs::write(path, serde_json::to_string_pretty(&fixture)? + "\n")?;
    println!("Generated 100 global founding sites and a short land crossing between workers");
    Ok(())
}
