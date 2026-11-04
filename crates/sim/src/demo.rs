//! Pure deterministic diagnostic world for P0.5; entities are not player cities.
use orbis_protocol::{EntityUpsert, LatLon, SetViewport};

#[derive(Debug, PartialEq, Eq, thiserror::Error)]
pub enum ViewportError {
    #[error("viewport_center_required")]
    MissingCenter,
    #[error("invalid_viewport")]
    Invalid,
}

pub fn snapshot(view: &SetViewport, count: usize) -> Result<Vec<EntityUpsert>, ViewportError> {
    let center = view.center.as_ref().ok_or(ViewportError::MissingCenter)?;
    if !center.lat.is_finite()
        || !center.lon.is_finite()
        || !view.zoom.is_finite()
        || !view.width_px.is_finite()
        || !view.height_px.is_finite()
        || center.lat.abs() > 90.0
        || center.lon.abs() > 180.0
        || !(0.0..=6.0).contains(&view.zoom)
        || !(1.0..=8192.0).contains(&view.width_px)
        || !(1.0..=8192.0).contains(&view.height_px)
    {
        return Err(ViewportError::Invalid);
    }
    let side = (count as f64).sqrt().ceil() as usize;
    let world_size = 256.0 * 2_f64.powf(view.zoom);
    let center_x = (center.lon + 180.0) / 360.0;
    let lat = center
        .lat
        .clamp(-85.0511287798066, 85.0511287798066)
        .to_radians();
    let center_y = (1.0 - lat.tan().asinh() / std::f64::consts::PI) / 2.0;
    let mut entities = Vec::new();
    for index in 0..count {
        let x = ((index % side) as f64 + 0.5) / side as f64;
        let y = ((index / side) as f64 + 0.5) / side as f64;
        if (x - center_x).abs() * world_size > view.width_px / 2.0
            || (y - center_y).abs() * world_size > view.height_px / 2.0
        {
            continue;
        }
        entities.push(EntityUpsert {
            id: format!("diagnostic-{index}"),
            kind: 1,
            pos: Some(LatLon {
                lat: (std::f64::consts::PI * (1.0 - 2.0 * y))
                    .sinh()
                    .atan()
                    .to_degrees(),
                lon: x * 360.0 - 180.0,
            }),
            kingdom_id: "diagnostic".into(),
        });
    }
    Ok(entities)
}

#[cfg(test)]
mod tests {
    use super::*;
    fn full() -> SetViewport {
        SetViewport {
            center: Some(LatLon { lat: 0.0, lon: 0.0 }),
            zoom: 0.0,
            width_px: 1024.0,
            height_px: 768.0,
        }
    }
    #[test]
    fn ten_thousand_entities_are_deterministic_and_unique() {
        let first = snapshot(&full(), 10_000).unwrap();
        assert_eq!(first.len(), 10_000);
        assert_eq!(first, snapshot(&full(), 10_000).unwrap());
        let ids: std::collections::HashSet<_> = first.iter().map(|e| &e.id).collect();
        assert_eq!(ids.len(), 10_000);
    }
    #[test]
    fn narrowed_view_culls_entities_and_nan_is_rejected() {
        let mut view = full();
        view.zoom = 6.0;
        assert!(snapshot(&view, 10_000).unwrap().len() < 100);
        view.zoom = f64::NAN;
        assert_eq!(snapshot(&view, 10_000), Err(ViewportError::Invalid));
    }
}
