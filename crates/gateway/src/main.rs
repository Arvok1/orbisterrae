use axum::{
    extract::{
        ws::{Message, WebSocket},
        DefaultBodyLimit, State, WebSocketUpgrade,
    },
    http::StatusCode,
    response::IntoResponse,
    routing::{get, post},
    Json, Router,
};
use orbis_core::world::AccountId;
use orbis_gateway::{
    auth::{Auth, Credentials, Tokens},
    world_service::{result, Context, ServiceError},
};
use orbis_protocol::{command, envelope, Envelope, ServerMsg};
use prost::Message as ProstMessage;
use std::{
    sync::Arc,
    time::{Duration, SystemTime, UNIX_EPOCH},
};

#[derive(Clone)]
struct App {
    nats: async_nats::Client,
    world: Context,
    auth: Arc<Auth>,
}
fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .expect("wall clock after epoch")
        .as_millis() as u64
}
#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt::init();
    let state = App {
        nats: async_nats::connect(
            std::env::var("NATS_URL").unwrap_or_else(|_| "nats://127.0.0.1:4222".into()),
        )
        .await?,
        world: Context::connect().await?,
        auth: Arc::new(Auth::new(Tokens::load()?)),
    };
    let app = Router::new()
        .route("/health", get(|| async { "orbis-gateway" }))
        .route("/ready", get(ready))
        .route("/ws", get(upgrade))
        .route("/api/register", post(register))
        .route("/api/login", post(login))
        .layer(DefaultBodyLimit::max(16 * 1024))
        .with_state(state);
    let bind = std::env::var("ORBIS_BIND").unwrap_or_else(|_| "127.0.0.1:8080".into());
    let address: std::net::SocketAddr = bind.parse()?;
    anyhow::ensure!(
        address.ip().is_loopback(),
        "local prototype requires a loopback bind address"
    );
    let listener = tokio::net::TcpListener::bind(address).await?;
    tracing::info!(%address,"Gateway ready");
    axum::serve(listener, app)
        .with_graceful_shutdown(async {
            let _ = tokio::signal::ctrl_c().await;
        })
        .await?;
    Ok(())
}
async fn register(
    State(app): State<App>,
    Json(credentials): Json<Credentials>,
) -> impl IntoResponse {
    auth_response(&app, credentials, true).await
}
async fn login(State(app): State<App>, Json(credentials): Json<Credentials>) -> impl IntoResponse {
    auth_response(&app, credentials, false).await
}
async fn auth_response(
    app: &App,
    credentials: Credentials,
    register: bool,
) -> axum::response::Response {
    match app
        .auth
        .authenticate(&app.world, credentials, register, now_ms())
        .await
    {
        Ok(session) => (StatusCode::OK, Json(session)).into_response(),
        Err(ServiceError::Unauthorized) => (
            StatusCode::UNAUTHORIZED,
            Json(serde_json::json!({"error":"invalid_credentials"})),
        )
            .into_response(),
        Err(ServiceError::BadInput(code)) => (
            StatusCode::BAD_REQUEST,
            Json(serde_json::json!({"error":code})),
        )
            .into_response(),
        Err(error) => {
            tracing::error!(%error,"Account request failed");
            (
                StatusCode::INTERNAL_SERVER_ERROR,
                Json(serde_json::json!({"error":"server_error"})),
            )
                .into_response()
        }
    }
}
async fn ready(State(app): State<App>) -> (StatusCode, &'static str) {
    let probe = Envelope {
        seq: 0,
        body: Some(envelope::Body::Command(orbis_protocol::Command {
            request_id: "readiness".into(),
            kind: None,
        })),
    }
    .encode_to_vec();
    for slot in 0..2 {
        if !matches!(
            tokio::time::timeout(
                Duration::from_secs(2),
                app.nats
                    .request(format!("orbis.shard.{slot}.commands"), probe.clone().into())
            )
            .await,
            Ok(Ok(_))
        ) {
            return (StatusCode::SERVICE_UNAVAILABLE, "worker-unavailable");
        }
    }
    (StatusCode::OK, "orbis-gateway-worker-ready")
}
async fn upgrade(ws: WebSocketUpgrade, State(app): State<App>) -> axum::response::Response {
    ws.max_message_size(65536)
        .max_frame_size(65536)
        .on_upgrade(move |socket| session(socket, app))
}
async fn session(mut socket: WebSocket, app: App) {
    let mut account: Option<AccountId> = None;
    while let Some(Ok(message)) = socket.recv().await {
        match message {
            Message::Binary(data) => {
                let Ok(envelope) = Envelope::decode(data.as_slice()) else {
                    break;
                };
                let Some(envelope::Body::Command(command)) = envelope.body.as_ref() else {
                    break;
                };
                let response = if let Some(command::Kind::Authenticate(auth)) = &command.kind {
                    account = None;
                    match app.auth.tokens.verify(&auth.token) {
                        Ok(id) => {
                            let role: Option<String> =
                                sqlx::query_scalar("SELECT role FROM accounts WHERE id=$1")
                                    .bind(id.0)
                                    .fetch_optional(&app.world.pool)
                                    .await
                                    .ok()
                                    .flatten();
                            if role.as_deref() == Some("admin")
                                || (role.as_deref() == Some("player")
                                    && app.world.actor_shard(id).await.is_ok())
                            {
                                account = Some(id);
                                result(&command.request_id, true, "")
                            } else {
                                result(&command.request_id, false, "authentication_required")
                            }
                        }
                        _ => result(&command.request_id, false, "authentication_required"),
                    }
                } else if let Some(id) = account {
                    if let Some(command::Kind::AdminSnapshot(request)) = &command.kind {
                        app.world
                            .admin_snapshot(id, request, now_ms())
                            .await
                            .unwrap_or_else(|error| {
                                if matches!(&error, ServiceError::Unauthorized) {
                                    result(&command.request_id, false, "admin_required")
                                } else {
                                    tracing::error!(%error, "admin snapshot failed");
                                    result(&command.request_id, false, "admin_unavailable")
                                }
                            })
                    } else {
                        forward(&app, id, &data, command).await
                    }
                } else if matches!(command.kind, Some(command::Kind::Viewport(_))) {
                    match tokio::time::timeout(
                        Duration::from_secs(3),
                        app.nats.request("orbis.demo.viewport", data.into()),
                    )
                    .await
                    {
                        Ok(Ok(reply)) => match Envelope::decode(reply.payload) {
                            Ok(envelope) => match envelope.body {
                                Some(envelope::Body::Server(server)) => server,
                                _ => result(&command.request_id, false, "invalid_worker_reply"),
                            },
                            Err(_) => result(&command.request_id, false, "invalid_worker_reply"),
                        },
                        _ => result(&command.request_id, false, "worker_unavailable"),
                    }
                } else {
                    result(&command.request_id, false, "authentication_required")
                };
                let bytes = Envelope {
                    seq: envelope.seq,
                    body: Some(envelope::Body::Server(response)),
                }
                .encode_to_vec();
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
async fn forward(
    app: &App,
    account: AccountId,
    data: &[u8],
    command: &orbis_protocol::Command,
) -> ServerMsg {
    for _ in 0..2 {
        let shard = match app.world.actor_shard(account).await {
            Ok(shard) => shard,
            Err(error) => {
                tracing::error!(%error, "actor shard lookup failed while forwarding");
                return result(&command.request_id, false, "authentication_required");
            }
        };
        let mut headers = async_nats::HeaderMap::new();
        headers.insert("x-orbis-account", account.0.to_string());
        let subject = format!("orbis.shard.{}.commands", orbis_sim::world::worker(shard));
        let reply = tokio::time::timeout(
            Duration::from_secs(5),
            app.nats
                .request_with_headers(subject, headers, data.to_vec().into()),
        )
        .await;
        if let Ok(Ok(reply)) = reply {
            if let Ok(Envelope {
                body: Some(envelope::Body::Server(server)),
                ..
            }) = Envelope::decode(reply.payload)
            {
                if matches!(&server.kind,Some(orbis_protocol::server_msg::Kind::Result(result)) if result.error=="shard_moved")
                {
                    continue;
                }
                return server;
            }
        }
        break;
    }
    result(&command.request_id, false, "worker_unavailable")
}
