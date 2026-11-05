//! Pure, deterministic S1.2 city simulation. The caller supplies IDs and time.
use std::collections::BTreeSet;

use orbis_core::{
    city::{
        Building, BuildingInstanceId, CityEvent, CityLayout, Construction, ConstructionId, Metrics,
        Population, CELL_COUNT, CITY_SIZE,
    },
    world::{City, CityId},
};
use serde::Deserialize;

const ZONE_RESIDENTIAL: u8 = 1;
const ZONE_AGRICULTURAL: u8 = 2;
const ZONE_INDUSTRIAL: u8 = 3;
const ZONE_CIVIC: u8 = 4;
const ZONE_MILITARY: u8 = 5;

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Catalog {
    pub building: Vec<BuildingDefinition>,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct BuildingDefinition {
    pub id: String,
    pub name_key: String,
    pub footprint: [u16; 2],
    pub construction_ms: u64,
    pub zones: Vec<String>,
    #[serde(default)]
    pub water_required: bool,
    #[serde(default)]
    pub housing: u32,
    #[serde(default)]
    pub food: u32,
    #[serde(default)]
    pub jobs_farmers: u32,
    #[serde(default)]
    pub jobs_artisans: u32,
    #[serde(default)]
    pub jobs_soldiers: u32,
    #[serde(default)]
    pub jobs_priests: u32,
    #[serde(default)]
    pub defense: u32,
    #[serde(default)]
    pub water_radius: u16,
    #[serde(default)]
    pub fire: f64,
    pub visual_role: String,
}

impl Catalog {
    pub fn parse(source: &str) -> Result<Self, toml::de::Error> {
        toml::from_str(source)
    }

    pub fn find(&self, id: &str) -> Option<&BuildingDefinition> {
        self.building.iter().find(|building| building.id == id)
    }

    pub fn validate(&self) -> Result<(), CityError> {
        let mut ids = BTreeSet::new();
        if self.building.len() != 15 {
            return Err(CityError("building_catalog_must_have_fifteen_entries"));
        }
        for building in &self.building {
            if !ids.insert(&building.id)
                || building.footprint.contains(&0)
                || building.construction_ms == 0
                || building.zones.is_empty()
                || building.zones.iter().any(|zone| zone_code(zone).is_none())
            {
                return Err(CityError("invalid_building_definition"));
            }
        }
        Ok(())
    }
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Balance {
    pub grid: GridBalance,
    pub simulation: SimulationBalance,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct GridBalance {
    pub size_by_tier: Vec<u16>,
    pub cell_m: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct SimulationBalance {
    pub tick_ms: u64,
    pub event_duration_ms: u64,
    pub base_growth_per_tick: u32,
    pub base_fire_per_million: u32,
    pub maximum_pending_construction: usize,
    pub initial_food_capacity: u32,
    pub initial_natural_water_coverage: f64,
}

impl Balance {
    pub fn parse(source: &str) -> Result<Self, toml::de::Error> {
        toml::from_str(source)
    }

    pub fn validate(&self) -> Result<(), CityError> {
        if self.grid.size_by_tier.first().copied() != Some(CITY_SIZE as u16)
            || !self.grid.cell_m.is_finite()
            || self.grid.cell_m <= 0.0
            || self.simulation.tick_ms == 0
            || self.simulation.event_duration_ms == 0
            || self.simulation.maximum_pending_construction == 0
            || !self.simulation.initial_natural_water_coverage.is_finite()
            || !(0.0..=1.0).contains(&self.simulation.initial_natural_water_coverage)
        {
            return Err(CityError("invalid_city_balance"));
        }
        Ok(())
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct CellEdit<'a> {
    pub x: u16,
    pub y: u16,
    pub layer: &'a str,
    pub value: &'a str,
}

#[derive(Clone, Copy, Debug)]
pub struct BuildingOrder<'a> {
    pub order: ConstructionId,
    pub instance: BuildingInstanceId,
    pub kind: &'a str,
    pub x: u16,
    pub y: u16,
    pub orientation: u8,
    pub due_at_ms: u64,
}

#[derive(Debug, PartialEq, Eq, thiserror::Error)]
#[error("{0}")]
pub struct CityError(pub &'static str);

pub fn encode_rle(cells: &[u8]) -> Vec<u8> {
    let mut encoded = Vec::new();
    let mut start = 0;
    while start < cells.len() {
        let value = cells[start];
        let mut end = start + 1;
        while end < cells.len() && cells[end] == value && end - start < u16::MAX as usize {
            end += 1;
        }
        let run = (end - start) as u16;
        encoded.push(value);
        encoded.extend_from_slice(&run.to_le_bytes());
        start = end;
    }
    encoded
}

pub fn decode_rle(encoded: &[u8], expected_len: usize) -> Result<Vec<u8>, CityError> {
    if !encoded.len().is_multiple_of(3) {
        return Err(CityError("invalid_rle"));
    }
    let mut decoded = Vec::with_capacity(expected_len);
    for chunk in encoded.as_chunks::<3>().0 {
        let run = u16::from_le_bytes([chunk[1], chunk[2]]) as usize;
        if run == 0 || decoded.len().saturating_add(run) > expected_len {
            return Err(CityError("invalid_rle"));
        }
        decoded.resize(decoded.len() + run, chunk[0]);
    }
    if decoded.len() != expected_len {
        return Err(CityError("invalid_rle"));
    }
    Ok(decoded)
}

pub fn initial_layout(
    city: &City,
    catalog: &Catalog,
    balance: &Balance,
) -> Result<CityLayout, CityError> {
    catalog.validate()?;
    let mut layout = CityLayout {
        city: city.id,
        kingdom: city.kingdom,
        width: CITY_SIZE as u16,
        height: CITY_SIZE as u16,
        terrain: vec![1; CELL_COUNT],
        roads: vec![0; CELL_COUNT],
        zones: vec![0; CELL_COUNT],
        buildings: Vec::new(),
        population: Population {
            farmers: city.population,
            artisans: 0,
            soldiers: 0,
            priests: 0,
        },
        metrics: Metrics::default(),
        events: Vec::new(),
        baseline_food_capacity: balance.simulation.initial_food_capacity,
        natural_water_coverage: balance.simulation.initial_natural_water_coverage,
        revision: 1,
        tick: 0,
    };
    let center = CITY_SIZE as u16 / 2;
    for y in 0..CITY_SIZE as u16 {
        layout.roads[index(center, y, layout.width)?] = 1;
    }
    let homes = [
        (center - 4, center - 3),
        (center + 2, center - 3),
        (center - 1, center + 2),
    ];
    for (ordinal, (x, y)) in homes.into_iter().enumerate() {
        paint_zone(&mut layout, x, y, 2, 2, ZONE_RESIDENTIAL)?;
        layout.buildings.push(Building {
            id: deterministic_building_id(city.id, ordinal as u8),
            kind: "house".to_owned(),
            x,
            y,
            orientation: 0,
            complete: true,
        });
    }
    recompute_metrics(&mut layout, catalog)?;
    Ok(layout)
}

pub fn apply_edits(
    layout: &CityLayout,
    expected_revision: u64,
    edits: &[CellEdit<'_>],
    catalog: &Catalog,
) -> Result<CityLayout, CityError> {
    check_revision(layout, expected_revision)?;
    let mut next = layout.clone();
    for edit in edits {
        let cell = index(edit.x, edit.y, layout.width)?;
        match edit.layer {
            "road" => {
                next.roads[cell] = match edit.value {
                    "none" => 0,
                    "road" => 1,
                    _ => return Err(CityError("invalid_road_value")),
                };
            }
            "zone" => {
                next.zones[cell] = if edit.value == "none" {
                    0
                } else {
                    zone_code(edit.value).ok_or(CityError("invalid_zone"))?
                };
            }
            _ => return Err(CityError("invalid_city_layer")),
        }
    }
    next.revision += 1;
    recompute_metrics(&mut next, catalog)?;
    Ok(next)
}

pub fn queue_building(
    layout: &CityLayout,
    expected_revision: u64,
    request: BuildingOrder<'_>,
    pending_count: usize,
    catalog: &Catalog,
    balance: &Balance,
) -> Result<(CityLayout, Construction), CityError> {
    check_revision(layout, expected_revision)?;
    if pending_count >= balance.simulation.maximum_pending_construction {
        return Err(CityError("construction_queue_full"));
    }
    let definition = catalog
        .find(request.kind)
        .ok_or(CityError("unknown_building"))?;
    if request.orientation > 3 {
        return Err(CityError("invalid_orientation"));
    }
    let (width, height) = oriented_footprint(definition.footprint, request.orientation);
    if request.x.saturating_add(width) > layout.width
        || request.y.saturating_add(height) > layout.height
    {
        return Err(CityError("building_out_of_bounds"));
    }
    let allowed = definition
        .zones
        .iter()
        .filter_map(|zone| zone_code(zone))
        .collect::<BTreeSet<_>>();
    for y in request.y..request.y + height {
        for x in request.x..request.x + width {
            let cell = index(x, y, layout.width)?;
            if !allowed.contains(&layout.zones[cell]) {
                return Err(CityError("building_requires_matching_zone"));
            }
        }
    }
    if definition.water_required && !has_water_access(layout, request.x, request.y, catalog)? {
        return Err(CityError("building_requires_water"));
    }
    if layout.buildings.iter().any(|building| {
        catalog.find(&building.kind).is_some_and(|existing| {
            rectangles_overlap(
                (request.x, request.y, width, height),
                building_rect(building, existing),
            )
        })
    }) {
        return Err(CityError("building_collision"));
    }
    let mut next = layout.clone();
    next.buildings.push(Building {
        id: request.instance,
        kind: request.kind.to_owned(),
        x: request.x,
        y: request.y,
        orientation: request.orientation,
        complete: false,
    });
    next.revision += 1;
    recompute_metrics(&mut next, catalog)?;
    Ok((
        next,
        Construction {
            id: request.order,
            city: layout.city,
            building: request.instance,
            kind: request.kind.to_owned(),
            due_at_ms: request.due_at_ms,
            status: "queued".to_owned(),
        },
    ))
}

pub fn cancel_construction(
    layout: &CityLayout,
    expected_revision: u64,
    order: &Construction,
    catalog: &Catalog,
) -> Result<CityLayout, CityError> {
    check_revision(layout, expected_revision)?;
    if order.status != "queued" {
        return Err(CityError("construction_not_pending"));
    }
    let mut next = layout.clone();
    let previous = next.buildings.len();
    next.buildings
        .retain(|building| building.id != order.building || building.complete);
    if next.buildings.len() == previous {
        return Err(CityError("construction_not_pending"));
    }
    next.revision += 1;
    recompute_metrics(&mut next, catalog)?;
    Ok(next)
}

pub fn complete_construction(
    layout: &CityLayout,
    building_id: BuildingInstanceId,
    catalog: &Catalog,
) -> Result<(CityLayout, bool), CityError> {
    let mut next = layout.clone();
    let Some(building) = next
        .buildings
        .iter_mut()
        .find(|building| building.id == building_id)
    else {
        return Err(CityError("construction_not_found"));
    };
    if building.complete {
        return Ok((next, false));
    }
    building.complete = true;
    next.revision += 1;
    recompute_metrics(&mut next, catalog)?;
    Ok((next, true))
}

pub fn simulate_tick(
    layout: &CityLayout,
    tick: u64,
    now_ms: u64,
    catalog: &Catalog,
    balance: &Balance,
) -> Result<(CityLayout, bool), CityError> {
    if tick <= layout.tick {
        return Ok((layout.clone(), false));
    }
    if tick != layout.tick + 1 {
        return Err(CityError("city_tick_out_of_order"));
    }
    let mut next = layout.clone();
    next.events.retain(|event| event.ends_at_ms > now_ms);
    recompute_metrics(&mut next, catalog)?;
    let population = next.population.total();
    let famine = next.metrics.food_capacity + f64::EPSILON < population as f64;
    if famine {
        push_event(
            &mut next,
            "famine",
            now_ms,
            balance.simulation.event_duration_ms,
        );
        next.population.farmers = next.population.farmers.saturating_sub(1);
    } else if next.metrics.housing_capacity > population as f64
        && next.metrics.water >= 1.0
        && next.metrics.density < 1.0
    {
        next.population.farmers = next
            .population
            .farmers
            .saturating_add(balance.simulation.base_growth_per_tick);
    }
    let fire_threshold = (next.metrics.fire_risk.clamp(0.0, 1.0)
        * balance.simulation.base_fire_per_million as f64) as u64;
    if deterministic_roll(layout.city, tick) % 1_000_000 < fire_threshold {
        push_event(
            &mut next,
            "fire",
            now_ms,
            balance.simulation.event_duration_ms,
        );
    }
    next.tick = tick;
    next.revision += 1;
    recompute_metrics(&mut next, catalog)?;
    Ok((next, true))
}

pub fn recompute_metrics(layout: &mut CityLayout, catalog: &Catalog) -> Result<(), CityError> {
    let mut occupied = 0_u32;
    let mut min_x = layout.width;
    let mut min_y = layout.height;
    let mut max_x = 0_u16;
    let mut max_y = 0_u16;
    let mut housing = 0_u32;
    let mut food = layout.baseline_food_capacity;
    let mut defense = 0_u32;
    let mut fire = 0.0;
    let mut wells = Vec::new();
    let mut completed = Vec::new();
    for building in layout.buildings.iter().filter(|building| building.complete) {
        let definition = catalog
            .find(&building.kind)
            .ok_or(CityError("unknown_building"))?;
        let (_, _, width, height) = building_rect(building, definition);
        occupied = occupied.saturating_add(u32::from(width) * u32::from(height));
        min_x = min_x.min(building.x);
        min_y = min_y.min(building.y);
        max_x = max_x.max(building.x + width);
        max_y = max_y.max(building.y + height);
        housing = housing.saturating_add(definition.housing);
        food = food.saturating_add(definition.food);
        defense = defense.saturating_add(definition.defense);
        fire += definition.fire;
        if definition.water_radius > 0 {
            wells.push((building.x, building.y, definition.water_radius));
        }
        completed.push((building, width, height));
    }
    let developed = if completed.is_empty() {
        0
    } else {
        u32::from(max_x - min_x) * u32::from(max_y - min_y)
    };
    let covered = completed
        .iter()
        .map(|(building, width, height)| {
            let center_x = building.x + *width / 2;
            let center_y = building.y + *height / 2;
            let wet = wells.iter().any(|(well_x, well_y, radius)| {
                let dx = center_x.abs_diff(*well_x) as u32;
                let dy = center_y.abs_diff(*well_y) as u32;
                dx * dx + dy * dy <= u32::from(*radius) * u32::from(*radius)
            });
            if wet {
                u32::from(*width) * u32::from(*height)
            } else {
                0
            }
        })
        .sum::<u32>();
    let density = if developed == 0 {
        0.0
    } else {
        occupied as f64 / developed as f64
    };
    let water = if occupied == 0 {
        layout.natural_water_coverage
    } else {
        (covered as f64 / occupied as f64 + layout.natural_water_coverage).min(1.0)
    };
    layout.metrics = Metrics {
        water,
        density,
        defense: defense as f64 * (1.0 + density),
        food_capacity: food as f64,
        housing_capacity: housing as f64,
        fire_risk: (fire * density * (1.25 - water)).clamp(0.0, 1.0),
    };
    allocate_population(layout, catalog)?;
    Ok(())
}

fn allocate_population(layout: &mut CityLayout, catalog: &Catalog) -> Result<(), CityError> {
    let total = layout.population.total();
    let mut artisan_jobs = 0_u32;
    let mut soldier_jobs = 0_u32;
    let mut priest_jobs = 0_u32;
    for building in layout.buildings.iter().filter(|building| building.complete) {
        let definition = catalog
            .find(&building.kind)
            .ok_or(CityError("unknown_building"))?;
        artisan_jobs = artisan_jobs.saturating_add(definition.jobs_artisans);
        soldier_jobs = soldier_jobs.saturating_add(definition.jobs_soldiers);
        priest_jobs = priest_jobs.saturating_add(definition.jobs_priests);
    }
    let mut remaining = total;
    let soldiers = remaining.min(soldier_jobs);
    remaining -= soldiers;
    let priests = remaining.min(priest_jobs);
    remaining -= priests;
    let artisans = remaining.min(artisan_jobs);
    remaining -= artisans;
    layout.population = Population {
        farmers: remaining,
        artisans,
        soldiers,
        priests,
    };
    Ok(())
}

fn check_revision(layout: &CityLayout, expected: u64) -> Result<(), CityError> {
    if layout.revision == expected {
        Ok(())
    } else {
        Err(CityError("stale_city_revision"))
    }
}

fn zone_code(zone: &str) -> Option<u8> {
    match zone {
        "residential" => Some(ZONE_RESIDENTIAL),
        "agricultural" => Some(ZONE_AGRICULTURAL),
        "industrial" => Some(ZONE_INDUSTRIAL),
        "civic" => Some(ZONE_CIVIC),
        "military" => Some(ZONE_MILITARY),
        _ => None,
    }
}

fn index(x: u16, y: u16, width: u16) -> Result<usize, CityError> {
    if x >= width || y >= CITY_SIZE as u16 {
        return Err(CityError("city_cell_out_of_bounds"));
    }
    Ok(usize::from(y) * usize::from(width) + usize::from(x))
}

fn paint_zone(
    layout: &mut CityLayout,
    x: u16,
    y: u16,
    width: u16,
    height: u16,
    zone: u8,
) -> Result<(), CityError> {
    for cell_y in y..y + height {
        for cell_x in x..x + width {
            let cell = index(cell_x, cell_y, layout.width)?;
            layout.zones[cell] = zone;
        }
    }
    Ok(())
}

fn deterministic_building_id(city: CityId, ordinal: u8) -> BuildingInstanceId {
    let mut bytes = *city.0.as_bytes();
    bytes[15] ^= ordinal.saturating_add(1);
    BuildingInstanceId(uuid::Uuid::from_bytes(bytes))
}

fn deterministic_roll(city: CityId, tick: u64) -> u64 {
    let mut value =
        tick ^ u64::from_le_bytes(city.0.as_bytes()[..8].try_into().expect("uuid bytes"));
    value ^= value >> 30;
    value = value.wrapping_mul(0xbf58_476d_1ce4_e5b9);
    value ^= value >> 27;
    value = value.wrapping_mul(0x94d0_49bb_1331_11eb);
    value ^ (value >> 31)
}

fn oriented_footprint(footprint: [u16; 2], orientation: u8) -> (u16, u16) {
    if orientation.is_multiple_of(2) {
        (footprint[0], footprint[1])
    } else {
        (footprint[1], footprint[0])
    }
}

fn building_rect(building: &Building, definition: &BuildingDefinition) -> (u16, u16, u16, u16) {
    let (width, height) = oriented_footprint(definition.footprint, building.orientation);
    (building.x, building.y, width, height)
}

fn rectangles_overlap(a: (u16, u16, u16, u16), b: (u16, u16, u16, u16)) -> bool {
    a.0 < b.0 + b.2 && a.0 + a.2 > b.0 && a.1 < b.1 + b.3 && a.1 + a.3 > b.1
}

fn has_water_access(
    layout: &CityLayout,
    x: u16,
    y: u16,
    catalog: &Catalog,
) -> Result<bool, CityError> {
    for building in layout.buildings.iter().filter(|building| building.complete) {
        let definition = catalog
            .find(&building.kind)
            .ok_or(CityError("unknown_building"))?;
        if definition.water_radius > 0 {
            let dx = x.abs_diff(building.x) as u32;
            let dy = y.abs_diff(building.y) as u32;
            let radius = u32::from(definition.water_radius);
            if dx * dx + dy * dy <= radius * radius {
                return Ok(true);
            }
        }
    }
    Ok(false)
}

fn push_event(layout: &mut CityLayout, kind: &str, now_ms: u64, duration_ms: u64) {
    if let Some(event) = layout.events.iter_mut().find(|event| event.kind == kind) {
        event.ends_at_ms = now_ms.saturating_add(duration_ms);
    } else {
        layout.events.push(CityEvent {
            kind: kind.to_owned(),
            started_at_ms: now_ms,
            ends_at_ms: now_ms.saturating_add(duration_ms),
        });
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use uuid::Uuid;

    fn catalog() -> Catalog {
        Catalog::parse(include_str!("../../../data/buildings/mvp.toml")).expect("catalog")
    }

    fn balance() -> Balance {
        Balance::parse(include_str!("../../../config/balance/city.toml")).expect("balance")
    }

    fn city() -> City {
        City {
            id: CityId(Uuid::from_u128(1)),
            kingdom: orbis_core::world::KingdomId(Uuid::from_u128(2)),
            name: "Vila Aurora".to_owned(),
            pos: orbis_core::world::Position { lat: 1.0, lon: 2.0 },
            tier: 0,
            ready_at_ms: 0,
            active: true,
            population: 12,
        }
    }

    #[test]
    fn rle_round_trip_and_rejects_invalid_lengths() {
        let mut cells = vec![0; CELL_COUNT];
        cells[91] = 3;
        cells[92] = 3;
        let encoded = encode_rle(&cells);
        assert_eq!(decode_rle(&encoded, CELL_COUNT), Ok(cells));
        assert_eq!(
            decode_rle(&encoded[..encoded.len() - 1], CELL_COUNT),
            Err(CityError("invalid_rle"))
        );
    }

    #[test]
    fn initial_village_has_three_houses_and_central_road() {
        let layout = initial_layout(&city(), &catalog(), &balance()).expect("layout");
        assert_eq!(layout.buildings.len(), 3);
        assert!(layout
            .buildings
            .iter()
            .all(|building| building.kind == "house"));
        assert_eq!(
            layout.roads.iter().filter(|cell| **cell == 1).count(),
            CITY_SIZE
        );
    }

    #[test]
    fn revisions_zones_collision_and_completion_are_authoritative() {
        let catalog = catalog();
        let balance = balance();
        let layout = initial_layout(&city(), &catalog, &balance).expect("layout");
        let layout = apply_edits(
            &layout,
            layout.revision,
            &[
                CellEdit {
                    x: 10,
                    y: 10,
                    layer: "zone",
                    value: "residential",
                },
                CellEdit {
                    x: 11,
                    y: 10,
                    layer: "zone",
                    value: "residential",
                },
                CellEdit {
                    x: 10,
                    y: 11,
                    layer: "zone",
                    value: "residential",
                },
                CellEdit {
                    x: 11,
                    y: 11,
                    layer: "zone",
                    value: "residential",
                },
            ],
            &catalog,
        )
        .expect("zoning");
        let request = BuildingOrder {
            order: ConstructionId(Uuid::from_u128(3)),
            instance: BuildingInstanceId(Uuid::from_u128(4)),
            kind: "house",
            x: 10,
            y: 10,
            orientation: 0,
            due_at_ms: 100,
        };
        let (queued, order) =
            queue_building(&layout, layout.revision, request, 0, &catalog, &balance)
                .expect("queue");
        assert_eq!(
            queue_building(&layout, layout.revision - 1, request, 0, &catalog, &balance),
            Err(CityError("stale_city_revision"))
        );
        assert_eq!(
            queue_building(
                &queued,
                queued.revision,
                BuildingOrder {
                    instance: BuildingInstanceId(Uuid::from_u128(5)),
                    ..request
                },
                1,
                &catalog,
                &balance
            ),
            Err(CityError("building_collision"))
        );
        let (complete, changed) =
            complete_construction(&queued, order.building, &catalog).expect("complete");
        assert!(changed);
        assert!(
            !complete_construction(&complete, order.building, &catalog)
                .expect("idempotent")
                .1
        );
    }

    #[test]
    fn placement_changes_density_and_defense_with_same_buildings() {
        let catalog = catalog();
        let base = initial_layout(&city(), &catalog, &balance()).expect("layout");
        let mut compact = base.clone();
        let mut spread = base.clone();
        let tower = |id, x, y| Building {
            id: BuildingInstanceId(Uuid::from_u128(id)),
            kind: "tower".to_owned(),
            x,
            y,
            orientation: 0,
            complete: true,
        };
        compact
            .buildings
            .extend([tower(8, 42, 42), tower(9, 45, 42)]);
        spread.buildings.extend([tower(8, 4, 4), tower(9, 88, 88)]);
        recompute_metrics(&mut compact, &catalog).expect("metrics");
        recompute_metrics(&mut spread, &catalog).expect("metrics");
        assert!(compact.metrics.density > spread.metrics.density);
        assert!(compact.metrics.defense > spread.metrics.defense);
    }

    #[test]
    fn same_seeded_tick_is_deterministic_and_idempotent() {
        let catalog = catalog();
        let balance = balance();
        let layout = initial_layout(&city(), &catalog, &balance).expect("layout");
        let first = simulate_tick(&layout, 1, 1000, &catalog, &balance).expect("tick");
        let again = simulate_tick(&layout, 1, 1000, &catalog, &balance).expect("tick");
        assert_eq!(first, again);
        assert!(
            !simulate_tick(&first.0, 1, 2000, &catalog, &balance)
                .expect("idempotent")
                .1
        );
    }

    fn building(id: u128, kind: &str, x: u16, y: u16) -> Building {
        Building {
            id: BuildingInstanceId(Uuid::from_u128(id)),
            kind: kind.to_owned(),
            x,
            y,
            orientation: 0,
            complete: true,
        }
    }

    #[test]
    fn well_coverage_and_housing_shape_population_growth() {
        let catalog = catalog();
        let balance = balance();
        let base = initial_layout(&city(), &catalog, &balance).expect("layout");
        // A: well covering every building, one extra house and a field.
        let mut a = base.clone();
        a.buildings.extend([
            building(30, "well", 44, 44),
            building(31, "house", 42, 38),
            building(32, "field", 52, 42),
        ]);
        recompute_metrics(&mut a, &catalog).expect("a");
        assert_eq!(a.metrics.water, 1.0);
        // B: untouched village without a well.
        let mut b = base.clone();
        recompute_metrics(&mut b, &catalog).expect("b");
        assert_eq!(b.metrics.water, 0.5);
        // C: a well that sits out of reach of every building.
        let mut c = base.clone();
        c.buildings
            .extend([building(40, "well", 4, 4), building(41, "house", 42, 38)]);
        recompute_metrics(&mut c, &catalog).expect("c");
        assert!(c.metrics.water < 1.0);
        for tick in 1..=40 {
            for layout in [&mut a, &mut b, &mut c] {
                let (next, changed) =
                    simulate_tick(layout, tick, tick * 1000, &catalog, &balance).expect("tick");
                assert!(changed);
                *layout = next;
            }
        }
        assert_eq!(b.population.total(), 12, "no well means no growth");
        assert_eq!(
            c.population.total(),
            12,
            "partial water coverage blocks growth"
        );
        assert_eq!(
            a.population.total(),
            32,
            "growth stops exactly at housing capacity"
        );
    }

    #[test]
    fn famine_fires_when_population_exceeds_food_and_reduces_farmers() {
        let catalog = catalog();
        let balance = balance();
        let mut layout = initial_layout(&city(), &catalog, &balance).expect("layout");
        layout.population.farmers = 40;
        let (next, changed) = simulate_tick(&layout, 1, 1000, &catalog, &balance).expect("tick");
        assert!(changed);
        assert!(next.events.iter().any(|event| event.kind == "famine"));
        assert_eq!(next.population.farmers, 39);
    }

    #[test]
    fn dense_workshops_ignite_deterministically_within_a_bounded_horizon() {
        let catalog = catalog();
        let balance = balance();
        let mut layout = initial_layout(&city(), &catalog, &balance).expect("layout");
        for index in 0..12_u128 {
            let x = 52 + (index % 4) as u16 * 3;
            let y = 45 + (index / 4) as u16 * 3;
            layout.buildings.push(building(100 + index, "forge", x, y));
        }
        recompute_metrics(&mut layout, &catalog).expect("metrics");
        assert_eq!(layout.metrics.fire_risk, 1.0);
        let horizon = 50_000_u64;
        let mut current = layout;
        let mut ignited = false;
        for tick in 1..=horizon {
            let (next, _) =
                simulate_tick(&current, tick, tick * 1000, &catalog, &balance).expect("tick");
            current = next;
            if current.events.iter().any(|event| event.kind == "fire") {
                ignited = true;
                break;
            }
        }
        assert!(
            ignited,
            "fire must ignite within {horizon} deterministic ticks"
        );
    }

    #[test]
    fn invalid_edits_water_requirements_and_cancellation_are_enforced() {
        let catalog = catalog();
        let balance = balance();
        let layout = initial_layout(&city(), &catalog, &balance).expect("layout");
        let edit = |x, y, layer, value| CellEdit { x, y, layer, value };
        assert_eq!(
            apply_edits(
                &layout,
                layout.revision,
                &[edit(0, 0, "zone", "commerce")],
                &catalog
            ),
            Err(CityError("invalid_zone"))
        );
        assert_eq!(
            apply_edits(
                &layout,
                layout.revision,
                &[edit(0, 0, "road", "highway")],
                &catalog
            ),
            Err(CityError("invalid_road_value"))
        );
        assert_eq!(
            apply_edits(
                &layout,
                layout.revision,
                &[edit(96, 0, "road", "road")],
                &catalog
            ),
            Err(CityError("city_cell_out_of_bounds"))
        );
        assert_eq!(
            apply_edits(
                &layout,
                layout.revision,
                &[edit(0, 0, "terrain", "grass")],
                &catalog
            ),
            Err(CityError("invalid_city_layer"))
        );
        let civic: Vec<CellEdit> = (20_u16..24)
            .flat_map(|x| (20_u16..23).map(move |y| edit(x, y, "zone", "civic")))
            .collect();
        let zoned = apply_edits(&layout, layout.revision, &civic, &catalog).expect("zone");
        let port = BuildingOrder {
            order: ConstructionId(Uuid::from_u128(50)),
            instance: BuildingInstanceId(Uuid::from_u128(51)),
            kind: "port",
            x: 20,
            y: 20,
            orientation: 0,
            due_at_ms: 100,
        };
        assert_eq!(
            queue_building(&zoned, zoned.revision, port, 0, &catalog, &balance),
            Err(CityError("building_requires_water"))
        );
        let mut with_well = zoned.clone();
        with_well.buildings.push(building(52, "well", 24, 22));
        let (queued, order) =
            queue_building(&with_well, with_well.revision, port, 0, &catalog, &balance)
                .expect("watered port");
        assert_eq!(queued.buildings.len(), 5);
        let canceled =
            cancel_construction(&queued, queued.revision, &order, &catalog).expect("cancel");
        assert!(!canceled
            .buildings
            .iter()
            .any(|building| building.id == order.building && !building.complete));
        assert_eq!(
            cancel_construction(&canceled, canceled.revision, &order, &catalog),
            Err(CityError("construction_not_pending"))
        );
        assert_eq!(
            complete_construction(&canceled, order.building, &catalog),
            Err(CityError("construction_not_found"))
        );
    }

    #[test]
    fn seeded_scripts_preserve_population_and_metric_invariants() {
        let catalog = catalog();
        let balance = balance();
        for seed in 0..8_u128 {
            let mut town = city();
            town.id = CityId(Uuid::from_u128(seed + 1000));
            let mut layout = initial_layout(&town, &catalog, &balance).expect("layout");
            let zoned = apply_edits(
                &layout,
                layout.revision,
                &(8_u16..10)
                    .flat_map(|x| {
                        (8_u16..10).map(move |y| CellEdit {
                            x,
                            y,
                            layer: "zone",
                            value: "residential",
                        })
                    })
                    .collect::<Vec<_>>(),
                &catalog,
            )
            .expect("zone");
            assert!(zoned.revision > layout.revision);
            layout = zoned;
            let (queued, order) = queue_building(
                &layout,
                layout.revision,
                BuildingOrder {
                    order: ConstructionId(Uuid::from_u128(seed + 2000)),
                    instance: BuildingInstanceId(Uuid::from_u128(seed + 3000)),
                    kind: "house",
                    x: 8,
                    y: 8,
                    orientation: 0,
                    due_at_ms: 1000,
                },
                0,
                &catalog,
                &balance,
            )
            .expect("queue");
            layout = queued;
            let (completed, changed) =
                complete_construction(&layout, order.building, &catalog).expect("complete");
            assert!(changed);
            layout = completed;
            for tick in 1..=30 {
                let (next, _) =
                    simulate_tick(&layout, tick, tick * 1000, &catalog, &balance).expect("tick");
                layout = next;
                assert_invariants(&layout);
            }
        }
    }

    fn assert_invariants(layout: &CityLayout) {
        let population = &layout.population;
        assert_eq!(
            population.farmers + population.artisans + population.soldiers + population.priests,
            population.total()
        );
        assert!((0.0..=1.0).contains(&layout.metrics.water));
        assert!((0.0..=1.0).contains(&layout.metrics.density));
        assert!(layout.metrics.food_capacity >= 0.0);
        assert!(layout.metrics.housing_capacity >= 0.0);
        assert!(population.total() as f64 <= layout.metrics.housing_capacity + 1.0);
    }
}
