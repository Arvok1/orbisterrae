use orbis_core::{
    time::FixedClock,
    world::{CityId, KingdomId, Position, Ruler, RulerId},
};
use orbis_sim::world::{self, Balance, Geography, TerritoryBalance, VillageOrder};
use std::collections::BTreeSet;

fn fixture() -> (Balance, TerritoryBalance, Geography, Ruler) {
    let balance: Balance =
        toml::from_str(include_str!("../../../config/balance/first_village.toml")).unwrap();
    let territory: TerritoryBalance =
        toml::from_str(include_str!("../../../config/balance/territory.toml")).unwrap();
    balance.validate().unwrap();
    territory.validate().unwrap();
    let geo = Geography::from_json(
        include_str!("../../../apps/client/src/assets/world-land-v1.json"),
        include_str!("../../../apps/client/src/assets/world-terrain-v1.json"),
    )
    .unwrap();
    let ruler = Ruler {
        id: RulerId(uuid::Uuid::from_u128(1)),
        kingdom: KingdomId(uuid::Uuid::from_u128(2)),
        pos: balance.spawn,
        supplies: balance.ruler.initial_supplies,
        journey: None,
        founding_until: None,
        city: None,
        revision: 0,
    };
    (balance, territory, geo, ruler)
}

#[test]
fn real_geography_rejects_ocean_and_lakes_and_accepts_paris_near_seine() {
    let (balance, _, geo, _) = fixture();
    assert!(geo.is_land(balance.spawn));
    assert!(geo.near_water(balance.spawn, balance.village.water_distance_m));
    assert!(!geo.is_land(Position {
        lat: 0.0,
        lon: -140.0
    }));
    assert!(!geo.is_land(Position {
        lat: 47.7,
        lon: -87.4
    }));
    assert!(!geo.is_land(Position {
        lat: f64::NAN,
        lon: 0.0
    }));
}

#[test]
fn movement_is_deterministic_and_old_arrival_cannot_complete_a_new_trip() {
    let (balance, _, geo, ruler) = fixture();
    let destination = Position {
        lat: 48.86,
        lon: 2.37,
    };
    let (moving, event, due) =
        world::plan_move(&ruler, destination, &FixedClock(100), &geo, &balance).unwrap();
    assert_eq!(
        moving,
        world::plan_move(&ruler, destination, &FixedClock(100), &geo, &balance)
            .unwrap()
            .0
    );
    assert_eq!(
        world::complete(&moving, None, &event, &FixedClock(due - 1)).0,
        moving
    );
    let arrived = world::complete(&moving, None, &event, &FixedClock(due)).0;
    assert_eq!(arrived.pos, destination);
    let (new_trip, _, new_due) =
        world::plan_move(&arrived, ruler.pos, &FixedClock(due), &geo, &balance).unwrap();
    assert_eq!(
        world::complete(&new_trip, None, &event, &FixedClock(new_due)).0,
        new_trip
    );
    assert_eq!(
        world::plan_move(
            &ruler,
            Position {
                lat: 0.0,
                lon: -140.0
            },
            &FixedClock(0),
            &geo,
            &balance
        )
        .unwrap_err()
        .0,
        "land_required"
    );
}

#[test]
fn village_cost_reach_spacing_and_completion_are_authoritative_and_idempotent() {
    let (balance, _, geo, ruler) = fixture();
    let order = || VillageOrder {
        id: CityId(uuid::Uuid::from_u128(3)),
        name: "Primeira vila",
        pos: ruler.pos,
    };
    let (founding, city, event) =
        world::plan_village(&ruler, order(), &[], &FixedClock(1000), &geo, &balance).unwrap();
    assert_eq!(founding.supplies, ruler.supplies - balance.village.cost);
    assert!(!city.active);
    let (done, town) = world::complete(
        &founding,
        Some(&city),
        &event,
        &FixedClock(city.ready_at_ms),
    );
    assert!(town.as_ref().unwrap().active);
    assert!(done.founding_until.is_none());
    assert_eq!(
        world::complete(&done, town.as_ref(), &event, &FixedClock(city.ready_at_ms)),
        (done.clone(), town)
    );
    assert_eq!(
        world::plan_village(&ruler, order(), &[city], &FixedClock(1000), &geo, &balance)
            .unwrap_err()
            .0,
        "city_too_close"
    );
    assert_eq!(
        world::plan_village(&done, order(), &[], &FixedClock(1000), &geo, &balance)
            .unwrap_err()
            .0,
        "first_village_already_founded"
    );
}

#[test]
fn h3_incremental_ownership_matches_full_and_has_one_owner_per_cell() {
    let (balance, territory, geo, ruler) = fixture();
    let order = VillageOrder {
        id: CityId(uuid::Uuid::from_u128(3)),
        name: "Vila",
        pos: ruler.pos,
    };
    let (_, mut city, _) =
        world::plan_village(&ruler, order, &[], &FixedClock(0), &geo, &balance).unwrap();
    city.active = true;
    let mut neighbour = city.clone();
    neighbour.id = CityId(uuid::Uuid::from_u128(4));
    neighbour.pos.lon += 0.08;
    let old = world::territory(&[city.clone()], &territory, &geo, None);
    let affected: BTreeSet<_> = world::footprint(&neighbour, &territory, &geo)
        .into_iter()
        .map(|(id, _)| id)
        .collect();
    let cities = [city, neighbour];
    let mut incremental = old;
    incremental.extend(world::territory(&cities, &territory, &geo, Some(&affected)));
    let full = world::territory(&cities, &territory, &geo, None);
    assert!(!full.is_empty());
    assert_eq!(incremental, full);
    for cell in full.keys() {
        assert!((5..=6).contains(&world::boundary(*cell).len()));
    }
    let mut reversed = cities.to_vec();
    reversed.reverse();
    assert_eq!(world::territory(&reversed, &territory, &geo, None), full);
}

#[derive(serde::Deserialize)]
struct Transports {
    caravan: Option<orbis_sim::economy::TransportBalance>,
    ship: Option<orbis_sim::economy::TransportBalance>,
}

#[test]
fn shared_transport_values_match_economy_toml() {
    // `first_village.toml` duplicate os números de transporte da `economy.toml`
    // para que `sim::world::Balance` também os exija; um teste garante que os
    // dois arquivos permanecem idênticos.
    let prototype: Transports =
        toml::from_str(include_str!("../../../config/balance/first_village.toml")).unwrap();
    let economy: Transports =
        toml::from_str(include_str!("../../../config/balance/economy.toml")).unwrap();
    assert!(prototype.caravan.is_some() && prototype.ship.is_some());
    assert_eq!(prototype.caravan, economy.caravan);
    assert_eq!(prototype.ship, economy.ship);
}
