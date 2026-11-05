use crate::world::{CityId, KingdomId};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

pub const CITY_SIZE: usize = 96;
pub const CELL_COUNT: usize = CITY_SIZE * CITY_SIZE;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(transparent)]
pub struct BuildingInstanceId(pub Uuid);

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(transparent)]
pub struct ConstructionId(pub Uuid);

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct Building {
    pub id: BuildingInstanceId,
    pub kind: String,
    pub x: u16,
    pub y: u16,
    pub orientation: u8,
    pub complete: bool,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct Population {
    pub farmers: u32,
    pub artisans: u32,
    pub soldiers: u32,
    pub priests: u32,
}

impl Population {
    pub fn total(&self) -> u32 {
        self.farmers + self.artisans + self.soldiers + self.priests
    }
}

#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
pub struct Metrics {
    pub water: f64,
    pub density: f64,
    pub defense: f64,
    pub food_capacity: f64,
    pub housing_capacity: f64,
    pub fire_risk: f64,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct CityEvent {
    pub kind: String,
    pub started_at_ms: u64,
    pub ends_at_ms: u64,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct CityLayout {
    pub city: CityId,
    pub kingdom: KingdomId,
    pub width: u16,
    pub height: u16,
    pub terrain: Vec<u8>,
    pub roads: Vec<u8>,
    pub zones: Vec<u8>,
    pub buildings: Vec<Building>,
    pub population: Population,
    pub metrics: Metrics,
    pub events: Vec<CityEvent>,
    pub revision: u64,
    pub tick: u64,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct Construction {
    pub id: ConstructionId,
    pub city: CityId,
    pub building: BuildingInstanceId,
    pub kind: String,
    pub due_at_ms: u64,
    pub status: String,
}
