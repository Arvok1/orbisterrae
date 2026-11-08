//! One-shot NATS readiness probe (diagnostics only; not part of the harness).
use prost::Message;

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_max_level(tracing::Level::INFO)
        .init();
    let nats = async_nats::connect(
        std::env::var("NATS_URL").unwrap_or_else(|_| "nats://127.0.0.1:4222".into()),
    )
    .await?;
    let probe = orbis_protocol::Envelope {
        seq: 0,
        body: Some(orbis_protocol::envelope::Body::Command(
            orbis_protocol::Command {
                request_id: "readiness".into(),
                kind: None,
            },
        )),
    }
    .encode_to_vec();
    for slot in 0..2u16 {
        let subject = format!("orbis.shard.{slot}.commands");
        match tokio::time::timeout(
            std::time::Duration::from_secs(3),
            nats.request(subject.clone(), probe.clone().into()),
        )
        .await
        {
            Ok(Ok(message)) => println!("slot {slot}: replied {} bytes", message.payload.len()),
            Ok(Err(error)) => println!("slot {slot}: error {error}"),
            Err(_) => println!("slot {slot}: timeout"),
        }
    }
    Ok(())
}
