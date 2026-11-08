//! Economy domain primitives for S1.3. Deterministic and I/O-free.
use std::collections::BTreeMap;

use serde::{Deserialize, Serialize};
use uuid::Uuid;

use crate::world::{CityId, KingdomId, Position};

pub use crate::world::Journey;

macro_rules! identifier {
    ($name:ident) => {
        #[derive(
            Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Hash, Serialize, Deserialize,
        )]
        #[serde(transparent)]
        pub struct $name(pub Uuid);
    };
}
identifier!(HubId);
identifier!(OrderId);
identifier!(CaravanId);
identifier!(ShipId);

pub type Stock = BTreeMap<String, u32>;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub enum Side {
    #[serde(rename = "buy")]
    Buy,
    #[serde(rename = "sell")]
    Sell,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct Hub {
    pub id: HubId,
    pub name: String,
    pub pos: Position,
    pub kingdom: Option<KingdomId>,
    pub stock: Stock,
    pub metal: u64,
    /// Hub fee taken as a fraction of every trade, from the balance config.
    #[serde(default)]
    pub fee: f64,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct TradeOrder {
    pub id: OrderId,
    pub hub: HubId,
    pub kingdom: KingdomId,
    pub commodity: String,
    pub side: Side,
    pub price: f64,
    pub quantity: u32,
    pub placed_at_ms: u64,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct Fill {
    pub buy: OrderId,
    pub sell: OrderId,
    pub commodity: String,
    pub quantity: u32,
    pub price: f64,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct Caravan {
    pub id: CaravanId,
    pub kingdom: KingdomId,
    pub from: CityId,
    pub to: HubId,
    pub journey: Option<Journey>,
    pub cargo: Stock,
    pub metal: u64,
    pub revision: u64,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct Ship {
    pub id: ShipId,
    pub kingdom: KingdomId,
    pub from: CityId,
    pub to: HubId,
    pub journey: Option<Journey>,
    pub cargo: Stock,
    pub metal: u64,
    pub revision: u64,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct PriceObservation {
    pub commodity: String,
    pub price: f64,
    pub observed_at_ms: u64,
}
