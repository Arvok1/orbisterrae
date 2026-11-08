//! Pure, deterministic S1.3 economy decisions. The caller supplies time and seed.
use std::collections::BTreeSet;

use orbis_core::{
    economy::{Fill, Hub, Side, Stock, TradeOrder},
    world::Position,
};
use serde::Deserialize;

use crate::world::Geography;

#[derive(Debug, PartialEq, Eq, thiserror::Error)]
#[error("{0}")]
pub struct EconomyError(pub &'static str);

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Catalog {
    pub commodity: Vec<CommodityDefinition>,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct CommodityDefinition {
    pub id: String,
    pub name_key: String,
    pub base_price: f64,
    pub weight_kg: f64,
    #[serde(default)]
    pub perishable_days: u32,
    pub demand_base: f64,
}

impl Catalog {
    pub fn parse(source: &str) -> Result<Self, toml::de::Error> {
        toml::from_str(source)
    }

    pub fn find(&self, id: &str) -> Option<&CommodityDefinition> {
        self.commodity.iter().find(|commodity| commodity.id == id)
    }

    pub fn validate(&self) -> Result<(), EconomyError> {
        let mut ids = BTreeSet::new();
        if self.commodity.len() != 8 {
            return Err(EconomyError("commodity_catalog_must_have_eight_entries"));
        }
        for commodity in &self.commodity {
            if !ids.insert(&commodity.id)
                || commodity.name_key.is_empty()
                || !commodity.base_price.is_finite()
                || commodity.base_price <= 0.0
                || !commodity.weight_kg.is_finite()
                || commodity.weight_kg <= 0.0
                || !commodity.demand_base.is_finite()
                || commodity.demand_base <= 0.0
            {
                return Err(EconomyError("invalid_commodity_definition"));
            }
        }
        Ok(())
    }
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct Balance {
    pub price: PriceBalance,
    pub caravan: TransportBalance,
    pub ship: TransportBalance,
    pub hub: HubBalance,
    pub money: MoneyBalance,
    pub hub_seed: HubSeedBalance,
    pub target: TargetBalance,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct PriceBalance {
    pub elasticity: f64,
    pub min_multiplier: f64,
    pub max_multiplier: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct TransportBalance {
    pub capacity_kg: f64,
    pub speed_mps: f64,
    pub minimum_trip_ms: u64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct HubBalance {
    pub default_fee: f64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct MoneyBalance {
    pub initial_city_metal: u64,
    pub initial_hub_metal: u64,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct HubSeedBalance {
    pub seed: u64,
    pub count: usize,
    pub step_deg: f64,
    pub min_separation_m: f64,
    pub liquidity_units: u32,
}

#[derive(Clone, Debug, Deserialize, PartialEq)]
pub struct TargetBalance {
    pub max_monthly_inflation: f64,
}

impl Balance {
    pub fn parse(source: &str) -> Result<Self, toml::de::Error> {
        toml::from_str(source)
    }

    pub fn validate(&self) -> Result<(), EconomyError> {
        if !self.price.elasticity.is_finite()
            || self.price.elasticity <= 0.0
            || !self.price.min_multiplier.is_finite()
            || !self.price.max_multiplier.is_finite()
            || self.price.min_multiplier <= 0.0
            || self.price.max_multiplier < self.price.min_multiplier
            || !self.caravan.capacity_kg.is_finite()
            || self.caravan.capacity_kg <= 0.0
            || !self.caravan.speed_mps.is_finite()
            || self.caravan.speed_mps <= 0.0
            || self.caravan.minimum_trip_ms == 0
            || !self.ship.capacity_kg.is_finite()
            || self.ship.capacity_kg <= 0.0
            || !self.ship.speed_mps.is_finite()
            || self.ship.speed_mps <= 0.0
            || self.ship.minimum_trip_ms == 0
            || !self.hub.default_fee.is_finite()
            || !(0.0..=1.0).contains(&self.hub.default_fee)
            || self.hub_seed.count == 0
            || self.hub_seed.liquidity_units == 0
            || !self.hub_seed.step_deg.is_finite()
            || !self.target.max_monthly_inflation.is_finite()
            || self.target.max_monthly_inflation <= 0.0
            || self.hub_seed.step_deg <= 0.0
            || !self.hub_seed.min_separation_m.is_finite()
            || self.hub_seed.min_separation_m <= 0.0
        {
            return Err(EconomyError("invalid_economy_balance"));
        }
        Ok(())
    }
}

/// Local price: scarcity raises the multiplier away from the base price.
pub fn local_price(
    base_price: f64,
    stock: u32,
    demand: f64,
    elasticity: f64,
    min_multiplier: f64,
    max_multiplier: f64,
) -> f64 {
    let ratio = demand / stock.max(1) as f64;
    base_price * ratio.powf(elasticity).clamp(min_multiplier, max_multiplier)
}

/// Straight-line travel duration; wind and currents arrive in later passes.
pub fn journey_due(
    from: Position,
    to: Position,
    speed_mps: f64,
    now_ms: u64,
    minimum_trip_ms: u64,
) -> Result<u64, EconomyError> {
    if !from.valid() || !to.valid() || !speed_mps.is_finite() || speed_mps <= 0.0 {
        return Err(EconomyError("invalid_journey"));
    }
    let millis = (from.distance_m(to) / speed_mps * 1000.0).ceil() as u64;
    Ok(now_ms.saturating_add(millis.max(minimum_trip_ms)))
}

/// Price-time matching of one incoming order against the resting book.
pub fn match_orders(book: &mut Vec<TradeOrder>, incoming: TradeOrder) -> Vec<Fill> {
    book.sort_by(|a, b| {
        let price_order = match incoming.side {
            Side::Buy => a.price.total_cmp(&b.price),
            Side::Sell => b.price.total_cmp(&a.price),
        };
        price_order
            .then(a.placed_at_ms.cmp(&b.placed_at_ms))
            .then(a.id.cmp(&b.id))
    });
    let mut fills = Vec::new();
    let mut remaining = incoming.quantity;
    let mut kept = Vec::new();
    for mut resting in book.drain(..) {
        if remaining == 0 {
            kept.push(resting);
            continue;
        }
        let price_acceptable = match incoming.side {
            Side::Buy => resting.price <= incoming.price,
            Side::Sell => resting.price >= incoming.price,
        };
        if !price_acceptable {
            kept.push(resting);
            continue;
        }
        let quantity = remaining.min(resting.quantity);
        let (buy, sell) = match incoming.side {
            Side::Buy => (incoming.id, resting.id),
            Side::Sell => (resting.id, incoming.id),
        };
        fills.push(Fill {
            buy,
            sell,
            commodity: incoming.commodity.clone(),
            quantity,
            price: resting.price,
        });
        remaining -= quantity;
        resting.quantity -= quantity;
        if resting.quantity > 0 {
            kept.push(resting);
        }
    }
    if remaining > 0 {
        kept.push(TradeOrder {
            quantity: remaining,
            ..incoming
        });
    }
    *book = kept;
    fills
}

/// A counterparty holding goods and metal for a trade.
pub struct Party<'a> {
    pub stock: &'a mut Stock,
    pub metal: &'a mut u64,
}

/// Moves goods and metal between a hub and a travelling party, charging a fee.
pub fn apply_trade(
    hub: Party<'_>,
    traveller: Party<'_>,
    commodity: &str,
    side: Side,
    quantity: u32,
    price: f64,
    fee_rate: f64,
) -> Result<(), EconomyError> {
    if quantity == 0
        || !price.is_finite()
        || price <= 0.0
        || !fee_rate.is_finite()
        || !(0.0..=1.0).contains(&fee_rate)
    {
        return Err(EconomyError("invalid_trade"));
    }
    let total = price * quantity as f64;
    match side {
        Side::Buy => {
            let cost = (total * (1.0 + fee_rate)).ceil() as u64;
            if hub.stock.get(commodity).copied().unwrap_or(0) < quantity {
                return Err(EconomyError("insufficient_stock"));
            }
            if *traveller.metal < cost {
                return Err(EconomyError("insufficient_metal"));
            }
            *hub.metal = hub.metal.saturating_add(cost);
            *traveller.metal = traveller.metal.saturating_sub(cost);
            subtract(hub.stock, commodity, quantity);
            *traveller.stock.entry(commodity.to_owned()).or_default() += quantity;
        }
        Side::Sell => {
            let proceeds = (total * (1.0 - fee_rate)).floor() as u64;
            if traveller.stock.get(commodity).copied().unwrap_or(0) < quantity {
                return Err(EconomyError("insufficient_cargo"));
            }
            if *hub.metal < proceeds {
                return Err(EconomyError("hub_insufficient_metal"));
            }
            *hub.metal = hub.metal.saturating_sub(proceeds);
            *traveller.metal = traveller.metal.saturating_add(proceeds);
            subtract(traveller.stock, commodity, quantity);
            *hub.stock.entry(commodity.to_owned()).or_default() += quantity;
        }
    }
    Ok(())
}

fn subtract(stock: &mut Stock, commodity: &str, quantity: u32) {
    let Some(remaining) = stock.get_mut(commodity) else {
        return;
    };
    *remaining -= quantity;
    if *remaining == 0 {
        stock.remove(commodity);
    }
}

/// Deterministic hub placement over a land grid, ordered by a seeded hash.
pub fn seed_hubs(
    geo: &Geography,
    seed: u64,
    count: usize,
    step_deg: f64,
    min_separation_m: f64,
) -> Vec<Position> {
    let mut candidates = Vec::new();
    let mut lat = -60.0;
    while lat <= 75.0 {
        let mut lon = -180.0;
        while lon < 180.0 {
            let position = Position { lat, lon };
            if geo.is_land(position) {
                candidates.push(position);
            }
            lon += step_deg;
        }
        lat += step_deg;
    }
    pick_hubs(&candidates, seed, count, min_separation_m)
}

/// Greedy deterministic selection from precomputed land candidates.
pub fn pick_hubs(
    candidates: &[Position],
    seed: u64,
    count: usize,
    min_separation_m: f64,
) -> Vec<Position> {
    let mut indices: Vec<usize> = (0..candidates.len()).collect();
    indices.sort_by_key(|index| (mix(seed.wrapping_add(*index as u64)), *index));
    let mut chosen: Vec<Position> = Vec::with_capacity(count);
    for index in indices {
        if chosen.len() == count {
            break;
        }
        let position = candidates[index];
        if chosen
            .iter()
            .all(|existing| existing.distance_m(position) >= min_separation_m)
        {
            chosen.push(position);
        }
    }
    chosen
}

fn mix(mut value: u64) -> u64 {
    value ^= value >> 30;
    value = value.wrapping_mul(0xbf58_476d_1ce4_e5b9);
    value ^= value >> 27;
    value = value.wrapping_mul(0x94d0_49bb_1331_11eb);
    value ^ (value >> 31)
}

/// Builds one IA hub with initial liquidity from the catalog and balance.
pub fn build_hub(
    position: Position,
    index: usize,
    seed: u64,
    balance: &Balance,
    catalog: &Catalog,
) -> Hub {
    let raw = (mix(seed) ^ index as u64) as u128;
    Hub {
        id: orbis_core::economy::HubId(uuid::Uuid::from_u128(raw)),
        name: format!("Entreposto {}", index + 1),
        pos: position,
        kingdom: None,
        stock: catalog
            .commodity
            .iter()
            .map(|commodity| (commodity.id.clone(), balance.hub_seed.liquidity_units))
            .collect(),
        metal: balance.money.initial_hub_metal,
        fee: balance.hub.default_fee,
    }
}

/// Builds seeded IA hubs with initial liquidity from the catalog and balance.
pub fn seeded_hubs(geo: &Geography, seed: u64, balance: &Balance, catalog: &Catalog) -> Vec<Hub> {
    seed_hubs(
        geo,
        seed,
        balance.hub_seed.count,
        balance.hub_seed.step_deg,
        balance.hub_seed.min_separation_m,
    )
    .into_iter()
    .enumerate()
    .map(|(index, position)| build_hub(position, index, seed, balance, catalog))
    .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use orbis_core::economy::{HubId, OrderId};
    use orbis_core::world::KingdomId;
    use uuid::Uuid;

    fn catalog() -> Catalog {
        Catalog::parse(include_str!("../../../data/economy/commodities.toml")).expect("catalog")
    }

    fn balance() -> Balance {
        Balance::parse(include_str!("../../../config/balance/economy.toml")).expect("balance")
    }

    fn position(lat: f64, lon: f64) -> Position {
        Position { lat, lon }
    }

    #[test]
    fn catalog_and_balance_are_valid_and_deterministic() {
        catalog().validate().expect("catalog");
        balance().validate().expect("balance");
        assert_eq!(catalog(), catalog());
    }

    #[test]
    fn price_falls_with_stock_and_respects_bounds() {
        let balance = balance();
        let catalog = catalog();
        let grain = catalog.find("grain").expect("grain");
        let abundant = local_price(
            grain.base_price,
            10_000,
            grain.demand_base,
            balance.price.elasticity,
            balance.price.min_multiplier,
            balance.price.max_multiplier,
        );
        let scarce = local_price(
            grain.base_price,
            1,
            grain.demand_base,
            balance.price.elasticity,
            balance.price.min_multiplier,
            balance.price.max_multiplier,
        );
        assert!(abundant < grain.base_price);
        assert!(scarce > grain.base_price);
        assert_eq!(scarce, grain.base_price * balance.price.max_multiplier);
        assert!(local_price(
            grain.base_price,
            0,
            grain.demand_base,
            balance.price.elasticity,
            balance.price.min_multiplier,
            balance.price.max_multiplier,
        )
        .is_finite());
    }

    #[test]
    fn matching_uses_price_time_priority_and_is_deterministic() {
        let kingdom = KingdomId(Uuid::from_u128(1));
        let hub = HubId(Uuid::from_u128(2));
        let mut book = vec![
            TradeOrder {
                id: OrderId(Uuid::from_u128(10)),
                hub,
                kingdom,
                commodity: "grain".into(),
                side: Side::Sell,
                price: 3.0,
                quantity: 5,
                placed_at_ms: 1,
            },
            TradeOrder {
                id: OrderId(Uuid::from_u128(11)),
                hub,
                kingdom,
                commodity: "grain".into(),
                side: Side::Sell,
                price: 2.0,
                quantity: 4,
                placed_at_ms: 2,
            },
        ];
        let incoming = TradeOrder {
            id: OrderId(Uuid::from_u128(20)),
            hub,
            kingdom,
            commodity: "grain".into(),
            side: Side::Buy,
            price: 2.5,
            quantity: 6,
            placed_at_ms: 3,
        };
        let original = book.clone();
        let first = match_orders(&mut book, incoming.clone());
        let mut working = original.clone();
        let second = match_orders(&mut working, incoming.clone());
        assert_eq!(first, second);
        assert_eq!(first.len(), 1);
        assert_eq!(first[0].sell, OrderId(Uuid::from_u128(11)));
        assert_eq!(first[0].quantity, 4);
        let untouched = book
            .iter()
            .find(|order| order.id == OrderId(Uuid::from_u128(10)))
            .expect("untouched sell");
        assert_eq!(untouched.quantity, 5);
        let resting = book
            .iter()
            .find(|order| order.side == Side::Buy)
            .expect("resting buy remainder");
        assert_eq!(resting.quantity, 2);
    }

    #[test]
    fn trade_exchanges_goods_and_metal_with_fee() {
        let balance = balance();
        let mut stock: Stock = [("grain".to_owned(), 10)].into_iter().collect();
        let mut metal = 1000;
        let mut cargo: Stock = Stock::new();
        let mut cargo_metal = 100;
        apply_trade(
            Party {
                stock: &mut stock,
                metal: &mut metal,
            },
            Party {
                stock: &mut cargo,
                metal: &mut cargo_metal,
            },
            "grain",
            Side::Buy,
            4,
            2.0,
            balance.hub.default_fee,
        )
        .expect("buy");
        assert_eq!(stock["grain"], 6);
        assert_eq!(cargo["grain"], 4);
        assert_eq!(cargo_metal, 100 - 9); // 8 + ceil(0.16) fee
        assert_eq!(metal, 1009);
        assert_eq!(
            apply_trade(
                Party {
                    stock: &mut stock,
                    metal: &mut metal,
                },
                Party {
                    stock: &mut cargo,
                    metal: &mut cargo_metal,
                },
                "grain",
                Side::Buy,
                7,
                2.0,
                balance.hub.default_fee,
            ),
            Err(EconomyError("insufficient_stock"))
        );
    }

    #[test]
    fn journey_due_scales_with_distance_and_respects_minimum() {
        let due =
            journey_due(position(0.0, 0.0), position(0.0, 0.1), 5.0, 1000, 1500).expect("due");
        assert!(due >= 2500);
        let short =
            journey_due(position(0.0, 0.0), position(0.0, 0.0), 5.0, 1000, 1500).expect("short");
        assert_eq!(short, 2500);
    }

    #[test]
    fn seeded_hubs_are_deterministic_and_separated() {
        let mut indices = [0_u64; 3];
        for (seed, slot) in [7_u64, 8].into_iter().zip(&mut indices) {
            *slot = mix(seed);
        }
        assert_ne!(indices[0], indices[1]);
        let candidates = vec![
            position(0.0, 0.0),
            position(0.1, 0.1),
            position(10.0, 10.0),
            position(-20.0, -40.0),
            position(30.0, 60.0),
        ];
        let hubs = pick_hubs(&candidates, 42, 3, 500_000.0);
        assert_eq!(hubs.len(), 3);
        for (index, hub) in hubs.iter().enumerate() {
            assert!(hubs[index + 1..]
                .iter()
                .all(|other| hub.distance_m(*other) >= 500_000.0));
        }
        let first = pick_hubs(&candidates, 42, 3, 500_000.0);
        let again = pick_hubs(&candidates, 42, 3, 500_000.0);
        assert_eq!(hubs, first);
        assert_eq!(hubs, again);
    }

    #[test]
    fn seeded_hubs_build_initial_liquidity() {
        let geo = Geography::from_json(
            r#"{"coordinates":[[[[-180,-70],[-180,70],[180,70],[180,-70],[-180,-70]]]]}"#,
            r#"{"rivers":[],"lakes":[]}"#,
        )
        .expect("geo");
        let hubs = seeded_hubs(&geo, 42, &balance(), &catalog());
        assert_eq!(hubs.len(), balance().hub_seed.count);
        assert!(hubs
            .iter()
            .all(|hub| hub.metal == balance().money.initial_hub_metal));
    }

    /// Acceptance property (S1.3): bots trading through the seeded hubs for
    /// seven simulated days conserve the barter metal and keep prices bounded,
    /// with no inflation beyond the monthly target at the stable checkpoints.
    #[test]
    fn bots_trade_without_inflation_for_seven_days() {
        let catalog = catalog();
        let balance = balance();
        let hub_count = balance.hub_seed.count;

        // Equivalent land sites far apart; pick_hubs chooses deterministically.
        let candidates: Vec<Position> = (0..8)
            .map(|index| position(0.0, index as f64 * 45.0 - 180.0))
            .collect();
        let positions = pick_hubs(
            &candidates,
            balance.hub_seed.seed,
            hub_count,
            balance.hub_seed.min_separation_m,
        );
        assert_eq!(positions.len(), hub_count);
        let mut hubs: Vec<Hub> = positions
            .into_iter()
            .enumerate()
            .map(|(index, pos)| build_hub(pos, index, balance.hub_seed.seed, &balance, &catalog))
            .collect();

        // One bot trader per hub with a city-scale wallet of barter metal.
        let wallets: u64 = balance.money.initial_hub_metal / 4;
        let mut traders: Vec<(Stock, u64)> =
            (0..hub_count).map(|_| (Stock::new(), wallets)).collect();

        let initial_stock =
            balance.hub_seed.liquidity_units * catalog.commodity.len() as u32 * hub_count as u32;
        let initial_metal: u64 =
            hubs.iter().map(|hub| hub.metal).sum::<u64>() + wallets * hub_count as u64;
        let lot = balance.hub_seed.liquidity_units / 4;
        let grain = catalog.find("grain").expect("grain");
        let price_now = |stock: u32| {
            local_price(
                grain.base_price,
                stock,
                grain.demand_base,
                balance.price.elasticity,
                balance.price.min_multiplier,
                balance.price.max_multiplier,
            )
        };
        let initial_price = price_now(balance.hub_seed.liquidity_units);

        for day in 0..7 {
            for index in 0..hub_count {
                let commodity = catalog.commodity[(day + index) % catalog.commodity.len()]
                    .id
                    .clone();
                let hub = &mut hubs[index];
                let (trader_cargo, trader_metal) = &mut traders[index];

                // Morning leg: the bot buys one lot from the hub at its quote.
                let price = price_now(hub.stock.get(&commodity).copied().unwrap_or(0));
                apply_trade(
                    Party {
                        stock: &mut hub.stock,
                        metal: &mut hub.metal,
                    },
                    Party {
                        stock: trader_cargo,
                        metal: trader_metal,
                    },
                    &commodity,
                    Side::Buy,
                    lot,
                    price,
                    hub.fee,
                )
                .expect("bot buy");
                // Evening leg: the bot sells the same lot back to the hub.
                let price = price_now(hub.stock.get(&commodity).copied().unwrap_or(0));
                apply_trade(
                    Party {
                        stock: &mut hub.stock,
                        metal: &mut hub.metal,
                    },
                    Party {
                        stock: trader_cargo,
                        metal: trader_metal,
                    },
                    &commodity,
                    Side::Sell,
                    lot,
                    price,
                    hub.fee,
                )
                .expect("bot sell");

                let stock = hub.stock.get(&commodity).copied().unwrap_or(0);
                assert_eq!(stock, balance.hub_seed.liquidity_units);
                let price = price_now(hub.stock.get("grain").copied().unwrap_or(0));
                assert!(price >= grain.base_price * balance.price.min_multiplier);
                assert!(price <= grain.base_price * balance.price.max_multiplier);
            }
            assert_eq!(
                hubs.iter()
                    .map(|hub| hub.stock.values().sum::<u32>())
                    .sum::<u32>(),
                initial_stock
            );
        }

        // Barter metal circulates but is never created nor destroyed.
        let final_metal: u64 = hubs.iter().map(|hub| hub.metal).sum::<u64>()
            + traders.iter().map(|(_, metal)| *metal).sum::<u64>();
        assert_eq!(final_metal, initial_metal);

        // At the stable checkpoint the price matches the starting quote.
        let final_price = price_now(hubs[0].stock.get("grain").copied().unwrap_or(0));
        assert!(
            final_price <= initial_price * (1.0 + balance.target.max_monthly_inflation),
            "price drifted upward by more than the monthly target"
        );
        assert!(
            final_price >= initial_price * (1.0 - balance.target.max_monthly_inflation),
            "price drifted downward by more than the monthly target"
        );
    }
}
