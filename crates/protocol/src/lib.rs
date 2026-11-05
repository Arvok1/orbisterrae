//! Wire types generated from packages/protocol/orbis.proto.
//! Generated prost code is isolated in its own module so clippy lints do not
//! fail the workspace on generated symbols; the public API is re-exported.
#[allow(clippy::all)]
mod generated {
    include!(concat!(env!("OUT_DIR"), "/orbis.v1.rs"));
}
pub use generated::*;

#[cfg(test)]
mod tests {
    use super::*;
    use prost::Message;

    #[test]
    fn shared_wire_fixture_round_trip() {
        let bytes = [8, 42, 18, 8, 10, 1, 120, 82, 3, 10, 1, 65];
        let envelope = Envelope::decode(bytes.as_slice()).unwrap();
        assert_eq!(envelope.seq, 42);
        let Some(envelope::Body::Command(command)) = &envelope.body else {
            panic!("missing command")
        };
        assert_eq!(command.request_id, "x");
        let Some(command::Kind::FoundCity(city)) = &command.kind else {
            panic!("missing city")
        };
        assert_eq!(city.name, "A");
        assert_eq!(envelope.encode_to_vec(), bytes);
    }

    #[test]
    fn max_sequence_and_unicode_round_trip() {
        let envelope = Envelope {
            seq: u64::MAX,
            body: Some(envelope::Body::Command(Command {
                request_id: "cidade".into(),
                kind: Some(command::Kind::FoundCity(FoundCity {
                    name: "São Paulo".into(),
                    pos: Some(LatLon {
                        lat: -23.55,
                        lon: -46.63,
                    }),
                })),
            })),
        };
        assert_eq!(
            Envelope::decode(envelope.encode_to_vec().as_slice()).unwrap(),
            envelope
        );
    }

    #[test]
    fn malformed_frame_rejected_unknown_field_accepted() {
        assert!(Envelope::decode([255].as_slice()).is_err());
        assert!(Envelope::decode([8, 1, 160, 6, 1].as_slice()).is_ok());
    }

    #[test]
    fn world_state_and_handoff_round_trip_preserve_revisions() {
        let transfer = EntityHandoff {
            entity_id: "ruler".into(),
            revision: u64::MAX,
            from_shard: 42,
            to_shard: 43,
            state: vec![0, 255, 1],
        };
        assert_eq!(
            EntityHandoff::decode(transfer.encode_to_vec().as_slice()).unwrap(),
            transfer
        );
        let snapshot = WorldSnapshot {
            ruler: Some(RulerState {
                id: "ruler".into(),
                revision: u64::MAX,
                pos: Some(LatLon {
                    lat: 48.85,
                    lon: 2.35,
                }),
                ..Default::default()
            }),
            now_ms: 1_790_000_000_000.0,
            ..Default::default()
        };
        assert_eq!(
            WorldSnapshot::decode(snapshot.encode_to_vec().as_slice()).unwrap(),
            snapshot
        );
    }
}
