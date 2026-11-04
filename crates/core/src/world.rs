use serde::{Deserialize, Serialize};
use uuid::Uuid;

macro_rules! identifier {
    ($name:ident) => {
        #[derive(
            Clone, Copy, Debug, PartialEq, Eq, PartialOrd, Ord, Hash, Serialize, Deserialize,
        )]
        #[serde(transparent)]
        pub struct $name(pub Uuid);
    };
}
identifier!(AccountId);
identifier!(KingdomId);
identifier!(RulerId);
identifier!(CityId);

#[derive(Clone, Copy, Debug, PartialEq, Serialize, Deserialize)]
pub struct Position {
    pub lat: f64,
    pub lon: f64,
}

impl Position {
    pub fn valid(self) -> bool {
        self.lat.is_finite()
            && self.lon.is_finite()
            && self.lat.abs() <= 85.0
            && self.lon.abs() <= 180.0
    }
    pub fn distance_m(self, other: Self) -> f64 {
        let a = ((other.lat - self.lat).to_radians() / 2.0).sin().powi(2)
            + self.lat.to_radians().cos()
                * other.lat.to_radians().cos()
                * ((other.lon - self.lon).to_radians() / 2.0).sin().powi(2);
        2.0 * 6_371_000.0 * a.clamp(0.0, 1.0).sqrt().asin()
    }
    pub fn interpolate(self, other: Self, t: f64) -> Self {
        let delta = (other.lon - self.lon + 540.0).rem_euclid(360.0) - 180.0;
        Self {
            lat: self.lat + (other.lat - self.lat) * t,
            lon: (self.lon + delta * t + 540.0).rem_euclid(360.0) - 180.0,
        }
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct Journey {
    pub from: Position,
    pub to: Position,
    pub start_ms: u64,
    pub due_ms: u64,
}

impl Journey {
    pub fn position(&self, now_ms: u64) -> Position {
        let fraction = now_ms.saturating_sub(self.start_ms) as f64
            / self.due_ms.saturating_sub(self.start_ms).max(1) as f64;
        self.from.interpolate(self.to, fraction.clamp(0.0, 1.0))
    }
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct Ruler {
    pub id: RulerId,
    pub kingdom: KingdomId,
    pub pos: Position,
    pub supplies: u32,
    pub journey: Option<Journey>,
    pub founding_until: Option<u64>,
    pub city: Option<CityId>,
    pub revision: u64,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct City {
    pub id: CityId,
    pub kingdom: KingdomId,
    pub name: String,
    pub pos: Position,
    pub tier: usize,
    pub ready_at_ms: u64,
    pub active: bool,
    pub population: u32,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub enum Action {
    Arrive { ruler: RulerId, revision: u64 },
    CompleteVillage { ruler: RulerId, city: CityId },
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn interpolation_crosses_dateline_and_respects_injected_time() {
        let journey = Journey {
            from: Position {
                lat: 0.0,
                lon: 179.0,
            },
            to: Position {
                lat: 0.0,
                lon: -179.0,
            },
            start_ms: 10,
            due_ms: 110,
        };
        assert_eq!(journey.position(60).lon, -180.0);
        assert_eq!(journey.position(0), journey.from);
        assert_eq!(journey.position(1000), journey.to);
        assert!((journey.from.distance_m(journey.to) - 222_390.0).abs() < 100.0);
    }
}
