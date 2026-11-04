pub trait Clock {
    fn now_ms(&self) -> u64;
}

pub struct FixedClock(pub u64);
impl Clock for FixedClock {
    fn now_ms(&self) -> u64 {
        self.0
    }
}

#[derive(Debug, thiserror::Error)]
#[error("invalid_calendar_configuration")]
pub struct CalendarError;

/// Calendar phase from an injected action clock and balance parameters.
pub fn season(
    clock: &impl Clock,
    epoch_ms: u64,
    real_days_per_year: f64,
    seasons: u32,
) -> Result<u32, CalendarError> {
    if !real_days_per_year.is_finite() || real_days_per_year <= 0.0 || seasons == 0 {
        return Err(CalendarError);
    }
    let year_ms = real_days_per_year * 86_400_000.0;
    let elapsed = clock.now_ms().saturating_sub(epoch_ms) as f64;
    Ok(((elapsed % year_ms) / year_ms * seasons as f64).floor() as u32)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn calendar_uses_injected_time_and_wraps() {
        assert_eq!(season(&FixedClock(86_400_000), 0, 4.0, 4).unwrap(), 1);
        assert_eq!(season(&FixedClock(4 * 86_400_000), 0, 4.0, 4).unwrap(), 0);
        assert!(season(&FixedClock(0), 0, f64::NAN, 4).is_err());
    }
}
