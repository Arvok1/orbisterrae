use axum::{
    extract::{
        ws::{Message, WebSocket},
        State, WebSocketUpgrade,
    },
    routing::get,
    Router,
};
use orbis_protocol::{envelope, server_msg, CommandResult, Envelope, ServerMsg};
use prost::Message as ProstMessage;
use std::time::Duration;

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt::init();
    let nats = async_nats::connect(
        std::env::var("NATS_URL").unwrap_or_else(|_| "nats://127.0.0.1:4222".into()),
    )
    .await?;
    let app = Router::new()
        .route("/health", get(|| async { "orbis-gateway" }))
        .route("/ready", get(ready))
        .route("/ws", get(upgrade))
        .with_state(nats);
    let listener = tokio::net::TcpListener::bind("127.0.0.1:8080").await?;
    tracing::info!("Gateway ready at http://127.0.0.1:8080");
    axum::serve(listener, app)
        .with_graceful_shutdown(async {
            let _ = tokio::signal::ctrl_c().await;
        })
        .await?;
    Ok(())
}

async fn ready(State(nats): State<async_nats::Client>) -> (axum::http::StatusCode, &'static str) {
    let probe = Envelope {
        seq: 0,
        body: Some(envelope::Body::Command(orbis_protocol::Command {
            request_id: "readiness".into(),
            kind: None,
        })),
    };
    match tokio::time::timeout(
        Duration::from_secs(2),
        nats.request("orbis.demo.viewport", probe.encode_to_vec().into()),
    )
    .await
    {
        Ok(Ok(_)) => (axum::http::StatusCode::OK, "orbis-gateway-worker-ready"),
        _ => (
            axum::http::StatusCode::SERVICE_UNAVAILABLE,
            "worker-unavailable",
        ),
    }
}

async fn upgrade(
    ws: WebSocketUpgrade,
    State(nats): State<async_nats::Client>,
) -> axum::response::Response {
    ws.max_message_size(65536)
        .max_frame_size(65536)
        .on_upgrade(move |socket| session(socket, nats))
}

async fn session(mut socket: WebSocket, nats: async_nats::Client) {
    while let Some(Ok(message)) = socket.recv().await {
        match message {
            Message::Binary(data) => {
                let Ok(envelope) = Envelope::decode(data.as_slice()) else {
                    break;
                };
                let Some(envelope::Body::Command(command)) = envelope.body.as_ref() else {
                    break;
                };
                let request_id = command.request_id.clone();
                let response = tokio::time::timeout(
                    Duration::from_secs(3),
                    nats.request("orbis.demo.viewport", data.into()),
                )
                .await;
                let bytes = match response {
                    Ok(Ok(reply)) => reply.payload.to_vec(),
                    _ => Envelope {
                        seq: envelope.seq,
                        body: Some(envelope::Body::Server(ServerMsg {
                            kind: Some(server_msg::Kind::Result(CommandResult {
                                request_id,
                                ok: false,
                                error: "worker_unavailable".into(),
                            })),
                        })),
                    }
                    .encode_to_vec(),
                };
                if socket.send(Message::Binary(bytes)).await.is_err() {
                    break;
                }
            }
            Message::Close(_) => break,
            Message::Ping(data) => {
                if socket.send(Message::Pong(data)).await.is_err() {
                    break;
                }
            }
            _ => {}
        }
    }
}
