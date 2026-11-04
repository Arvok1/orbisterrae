//! Pure S1.1 world decisions and geographic validation. No clock reads or I/O.
use h3o::{CellIndex, LatLng, Resolution};
use orbis_core::{
    time::Clock,
    world::{Action, City, CityId, Journey, Position, Ruler},
};
use serde::Deserialize;
use std::collections::{BTreeMap, BTreeSet};

#[derive(Debug, PartialEq, Eq, thiserror::Error)]
#[error("{0}")]
pub struct GameError(pub &'static str);

#[derive(Deserialize)]
pub struct Balance {
    pub ruler: RulerBalance,
    pub village: VillageBalance,
    pub spawn: Position,
}
#[derive(Deserialize)]
pub struct RulerBalance {
    pub initial_supplies: u32,
    pub travel_speed_mps: f64,
    pub max_trip_m: f64,
    pub minimum_trip_ms: u64,
    pub path_sample_m: f64,
}
#[derive(Deserialize)]
pub struct VillageBalance {
    pub cost: u32,
    pub construction_ms: u64,
    pub minimum_distance_m: f64,
    pub water_distance_m: f64,
    pub founding_reach_m: f64,
    pub initial_population: u32,
}
impl Balance {
    pub fn validate(&self) -> Result<(), GameError> {
        if [
            self.ruler.travel_speed_mps,
            self.ruler.max_trip_m,
            self.ruler.path_sample_m,
            self.village.minimum_distance_m,
            self.village.water_distance_m,
            self.village.founding_reach_m,
        ]
        .iter()
        .any(|v| !v.is_finite() || *v <= 0.0)
            || !self.spawn.valid()
            || self.village.construction_ms == 0
            || self.ruler.minimum_trip_ms == 0
        {
            return Err(GameError("invalid_balance"));
        }
        Ok(())
    }
}

type Ring = Vec<[f64; 2]>;
#[derive(Deserialize)]
struct Land {
    coordinates: Vec<Vec<Ring>>,
}
#[derive(Deserialize)]
struct Hydro {
    rivers: Vec<River>,
    lakes: Vec<Lake>,
}
#[derive(Deserialize)]
struct River {
    lines: Vec<Ring>,
}
#[derive(Deserialize)]
struct Lake {
    polygons: Vec<Vec<Ring>>,
}
pub struct Geography {
    land: Vec<Region>,
    lakes: Vec<Region>,
    water: Vec<Vec<Position>>,
}
struct Region {
    rings: Vec<Ring>,
    min: [f64; 2],
    max: [f64; 2],
}
impl Region {
    fn new(rings: Vec<Ring>) -> Self {
        let mut min = [f64::INFINITY; 2];
        let mut max = [f64::NEG_INFINITY; 2];
        if let Some(outer) = rings.first() {
            for p in outer {
                for axis in 0..2 {
                    min[axis] = min[axis].min(p[axis]);
                    max[axis] = max[axis].max(p[axis]);
                }
            }
        }
        Self { rings, min, max }
    }
    fn contains(&self, p: Position) -> bool {
        p.lon >= self.min[0]
            && p.lon <= self.max[0]
            && p.lat >= self.min[1]
            && p.lat <= self.max[1]
            && self.rings.first().is_some_and(|ring| inside(ring, p))
            && !self.rings.iter().skip(1).any(|ring| inside(ring, p))
    }
}
impl Geography {
    pub fn from_json(land: &str, hydro: &str) -> Result<Self, serde_json::Error> {
        let land: Land = serde_json::from_str(land)?;
        let hydro: Hydro = serde_json::from_str(hydro)?;
        let lakes = hydro
            .lakes
            .into_iter()
            .flat_map(|lake| lake.polygons)
            .collect::<Vec<_>>();
        let water = hydro
            .rivers
            .into_iter()
            .flat_map(|r| r.lines)
            .chain(lakes.iter().flat_map(|polygon| polygon.iter().cloned()))
            .chain(land.coordinates.iter().flat_map(|p| p.iter().cloned()))
            .map(|ring| {
                ring.into_iter()
                    .map(|[lon, lat]| Position { lat, lon })
                    .collect()
            })
            .collect();
        Ok(Self {
            land: land.coordinates.into_iter().map(Region::new).collect(),
            lakes: lakes.into_iter().map(Region::new).collect(),
            water,
        })
    }
    pub fn is_land(&self, p: Position) -> bool {
        p.valid()
            && self.land.iter().any(|region| region.contains(p))
            && !self.lakes.iter().any(|region| region.contains(p))
    }
    pub fn near_water(&self, p: Position, maximum_m: f64) -> bool {
        self.water.iter().any(|line| {
            line.windows(2)
                .any(|segment| segment_distance_m(p, segment[0], segment[1]) <= maximum_m)
        })
    }
    pub fn route_is_land(&self, from: Position, to: Position, sample_m: f64) -> bool {
        let steps = (from.distance_m(to) / sample_m).ceil().max(1.0) as usize;
        (0..=steps).all(|step| self.is_land(from.interpolate(to, step as f64 / steps as f64)))
    }
}
fn inside(ring: &[[f64; 2]], p: Position) -> bool {
    let mut result = false;
    for pair in ring.windows(2) {
        let ([ax, ay], [bx, by]) = (pair[0], pair[1]);
        if (ay > p.lat) != (by > p.lat) && p.lon < (bx - ax) * (p.lat - ay) / (by - ay) + ax {
            result = !result;
        }
    }
    result
}
fn segment_distance_m(p: Position, a: Position, b: Position) -> f64 {
    let longitude = |lon: f64| (lon - p.lon + 540.0).rem_euclid(360.0) - 180.0;
    let xscale = p.lat.to_radians().cos();
    let (ax, ay) = (longitude(a.lon) * xscale, a.lat - p.lat);
    let (bx, by) = (longitude(b.lon) * xscale, b.lat - p.lat);
    if (bx - ax).abs() > 180.0 * xscale {
        return p.distance_m(a).min(p.distance_m(b));
    }
    let length = (bx - ax).powi(2) + (by - ay).powi(2);
    let t = if length > 0.0 {
        (-(ax * (bx - ax) + ay * (by - ay)) / length).clamp(0.0, 1.0)
    } else {
        0.0
    };
    let dx = ax + t * (bx - ax);
    let dy = ay + t * (by - ay);
    dx.hypot(dy).to_radians() * 6_371_000.0
}

pub fn shard(p: Position) -> u64 {
    u64::from(
        LatLng::new(p.lat, p.lon)
            .expect("validated position")
            .to_cell(Resolution::Three),
    )
}
pub fn worker(shard: u64) -> u16 {
    (((shard >> 36) ^ (shard >> 45)) & 1) as u16
}

fn idle(ruler: &Ruler) -> Result<(), GameError> {
    if ruler.journey.is_some() || ruler.founding_until.is_some() {
        return Err(GameError("ruler_busy"));
    }
    Ok(())
}
pub fn plan_move(
    ruler: &Ruler,
    to: Position,
    clock: &impl Clock,
    geo: &Geography,
    balance: &Balance,
) -> Result<(Ruler, Action, u64), GameError> {
    idle(ruler)?;
    if !to.valid() || !geo.is_land(to) {
        return Err(GameError("land_required"));
    }
    let distance = ruler.pos.distance_m(to);
    if distance > balance.ruler.max_trip_m {
        return Err(GameError("destination_too_far"));
    }
    if !geo.route_is_land(ruler.pos, to, balance.ruler.path_sample_m) {
        return Err(GameError("land_route_required"));
    }
    let now = clock.now_ms();
    let due = now.saturating_add(
        ((distance / balance.ruler.travel_speed_mps * 1000.0).ceil() as u64)
            .max(balance.ruler.minimum_trip_ms),
    );
    let mut next = ruler.clone();
    next.journey = Some(Journey {
        from: ruler.pos,
        to,
        start_ms: now,
        due_ms: due,
    });
    next.revision += 1;
    let action = Action::Arrive {
        ruler: ruler.id,
        revision: next.revision,
    };
    Ok((next, action, due))
}
pub struct VillageOrder<'a> {
    pub id: CityId,
    pub name: &'a str,
    pub pos: Position,
}
pub fn plan_village(
    ruler: &Ruler,
    order: VillageOrder<'_>,
    cities: &[City],
    clock: &impl Clock,
    geo: &Geography,
    balance: &Balance,
) -> Result<(Ruler, City, Action), GameError> {
    let VillageOrder {
        id: city_id,
        name,
        pos,
    } = order;
    idle(ruler)?;
    if ruler.city.is_some() {
        return Err(GameError("first_village_already_founded"));
    }
    if name.trim().chars().count() < 2
        || name.trim().chars().count() > 48
        || name.chars().any(char::is_control)
    {
        return Err(GameError("invalid_city_name"));
    }
    if !geo.is_land(pos) {
        return Err(GameError("land_required"));
    }
    if ruler.pos.distance_m(pos) > balance.village.founding_reach_m {
        return Err(GameError("ruler_too_far"));
    }
    if !geo.near_water(pos, balance.village.water_distance_m) {
        return Err(GameError("water_required"));
    }
    if cities
        .iter()
        .any(|city| city.pos.distance_m(pos) < balance.village.minimum_distance_m)
    {
        return Err(GameError("city_too_close"));
    }
    if ruler.supplies < balance.village.cost {
        return Err(GameError("insufficient_supplies"));
    }
    let due = clock
        .now_ms()
        .saturating_add(balance.village.construction_ms);
    let mut next = ruler.clone();
    next.supplies -= balance.village.cost;
    next.founding_until = Some(due);
    next.city = Some(city_id);
    next.revision += 1;
    let city = City {
        id: city_id,
        kingdom: ruler.kingdom,
        name: name.trim().into(),
        pos,
        tier: 1,
        ready_at_ms: due,
        active: false,
        population: balance.village.initial_population,
    };
    Ok((
        next,
        city,
        Action::CompleteVillage {
            ruler: ruler.id,
            city: city_id,
        },
    ))
}
pub fn complete(
    ruler: &Ruler,
    city: Option<&City>,
    action: &Action,
    clock: &impl Clock,
) -> (Ruler, Option<City>) {
    let mut next = ruler.clone();
    let mut town = city.cloned();
    match action {
        Action::Arrive {
            ruler: id,
            revision,
        } if *id == ruler.id && *revision == ruler.revision => {
            if let Some(journey) = &ruler.journey {
                if journey.due_ms <= clock.now_ms() {
                    next.pos = journey.to;
                    next.journey = None;
                    next.revision += 1;
                }
            }
        }
        Action::CompleteVillage {
            ruler: id,
            city: city_id,
        } if *id == ruler.id => {
            if let Some(city) = town.as_mut().filter(|city| {
                city.id == *city_id && !city.active && city.ready_at_ms <= clock.now_ms()
            }) {
                city.active = true;
                next.founding_until = None;
                next.revision += 1;
            }
        }
        _ => {}
    }
    (next, town)
}

#[derive(Deserialize)]
pub struct TerritoryBalance {
    pub influence: Influence,
}
#[derive(Deserialize)]
pub struct Influence {
    pub base_by_tier: Vec<f64>,
    pub falloff_exponent: f64,
    pub max_radius_cells: Vec<u32>,
    pub min_owner_influence: f64,
}
impl TerritoryBalance {
    pub fn validate(&self) -> Result<(), GameError> {
        let influence = &self.influence;
        if influence.base_by_tier.len() != 4
            || influence.max_radius_cells.len() != 4
            || influence
                .base_by_tier
                .iter()
                .any(|v| !v.is_finite() || *v <= 0.0)
            || influence
                .max_radius_cells
                .iter()
                .any(|v| *v == 0 || *v > 100)
            || !influence.falloff_exponent.is_finite()
            || influence.falloff_exponent <= 0.0
            || !influence.min_owner_influence.is_finite()
            || influence.min_owner_influence <= 0.0
        {
            return Err(GameError("invalid_territory_balance"));
        }
        Ok(())
    }
}

pub fn footprint(city: &City, balance: &TerritoryBalance, geo: &Geography) -> Vec<(u64, f64)> {
    let center = LatLng::new(city.pos.lat, city.pos.lon)
        .expect("valid city")
        .to_cell(Resolution::Seven);
    let tier = city.tier.min(balance.influence.base_by_tier.len() - 1);
    center
        .grid_disk::<Vec<_>>(balance.influence.max_radius_cells[tier])
        .into_iter()
        .filter_map(|cell| {
            let p = LatLng::from(cell);
            if !geo.is_land(Position {
                lat: p.lat(),
                lon: p.lng(),
            }) {
                return None;
            }
            let distance = center.grid_distance(cell).ok()? as f64;
            let influence = balance.influence.base_by_tier[tier]
                / (1.0 + distance).powf(balance.influence.falloff_exponent);
            (influence >= balance.influence.min_owner_influence)
                .then_some((u64::from(cell), influence))
        })
        .collect()
}
pub fn territory(
    cities: &[City],
    balance: &TerritoryBalance,
    geo: &Geography,
    affected: Option<&BTreeSet<u64>>,
) -> BTreeMap<u64, (CityId, f64)> {
    let mut owners: BTreeMap<u64, (CityId, f64)> = BTreeMap::new();
    for city in cities.iter().filter(|city| city.active) {
        for (cell, influence) in footprint(city, balance, geo) {
            if affected.is_some_and(|cells| !cells.contains(&cell)) {
                continue;
            }
            let owner = owners.entry(cell).or_insert((city.id, influence));
            if influence > owner.1 || (influence == owner.1 && city.id < owner.0) {
                *owner = (city.id, influence);
            }
        }
    }
    owners
}
pub fn boundary(cell: u64) -> Vec<Position> {
    CellIndex::try_from(cell)
        .expect("validated H3 cell")
        .boundary()
        .iter()
        .map(|p| Position {
            lat: p.lat(),
            lon: p.lng(),
        })
        .collect()
}
