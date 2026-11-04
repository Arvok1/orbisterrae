use futures_util::StreamExt;
use orbis_protocol::{
    command, envelope, server_msg, CommandResult, Envelope, ServerMsg, ViewportSnapshot,
};
use prost::Message;

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt::init();
    let nats = async_nats::connect(
        std::env::var("NATS_URL").unwrap_or_else(|_| "nats://127.0.0.1:4222".into()),
    )
    .await?;
    let mut subscription = nats
        .queue_subscribe("orbis.demo.viewport", "demo-workers".into())
        .await?;
    nats.flush().await?;
    tracing::info!("Diagnostic shard worker ready");
    while let Some(message) = subscription.next().await {
        let Some(reply) = message.reply else { continue };
        let Ok(envelope) = Envelope::decode(message.payload) else {
            continue;
        };
        let Some(envelope::Body::Command(command)) = envelope.body else {
            continue;
        };
        let result = match command.kind {
            Some(command::Kind::Viewport(view)) => {
                orbis_sim::demo::snapshot(&view, 10_000).map_err(|error| error.to_string())
            }
            _ => Err("command_not_available_in_diagnostic_world".into()),
        };
        let kind = match result {
            Ok(entities) => server_msg::Kind::Snapshot(ViewportSnapshot { entities }),
            Err(error) => server_msg::Kind::Result(CommandResult {
                request_id: command.request_id,
                ok: false,
                error,
            }),
        };
        let response = Envelope {
            seq: envelope.seq,
            body: Some(envelope::Body::Server(ServerMsg { kind: Some(kind) })),
        };
        nats.publish(reply, response.encode_to_vec().into()).await?;
    }
    Ok(())
}
