use futures_util::FutureExt;
use orbis_core::world::{AccountId, Position, Ruler};
use orbis_gateway::{
    auth::{Auth, Credentials, Tokens},
    world_service::Context,
};
use orbis_protocol::{command, server_msg, Command, FoundCity, LatLon, MoveRuler, SetViewport};
use orbis_sim::world;
use sqlx::{postgres::PgConnectOptions, types::Json, Row};
use std::str::FromStr;
use uuid::Uuid;

fn view() -> Command {
    Command {
        request_id: "view".into(),
        kind: Some(command::Kind::Viewport(SetViewport {
            center: Some(LatLon {
                lat: 48.85,
                lon: 2.35,
            }),
            zoom: 3.0,
            width_px: 1280.0,
            height_px: 720.0,
        })),
    }
}
fn accepted(response: orbis_protocol::ServerMsg) {
    let Some(server_msg::Kind::Result(result)) = response.kind else {
        panic!("missing command result")
    };
    assert!(result.ok, "{}", result.error);
}

#[tokio::test]
#[ignore = "requires local PostGIS; creates and drops an isolated test database"]
async fn restart_replays_pending_actions_once_and_handoff_preserves_entity(
) -> Result<(), Box<dyn std::error::Error>> {
    let base = std::env::var("DATABASE_URL")
        .unwrap_or_else(|_| "postgres://orbis:orbis@127.0.0.1:5432/orbis?sslmode=disable".into());
    let options = PgConnectOptions::from_str(&base)?;
    let admin = sqlx::postgres::PgPoolOptions::new()
        .max_connections(1)
        .connect_with(options.clone().database("postgres"))
        .await?;
    let name = format!("orbis_test_{}", Uuid::new_v4().simple());
    sqlx::query(&format!("CREATE DATABASE {name}"))
        .execute(&admin)
        .await?;
    let pool = sqlx::postgres::PgPoolOptions::new()
        .max_connections(8)
        .connect_with(options.database(&name))
        .await?;
    sqlx::migrate!("../../infra/migrations").run(&pool).await?;
    let mut context = Context::connect().await?;
    context.pool.close().await;
    context.pool = pool;
    let result = std::panic::AssertUnwindSafe(exercise(&mut context))
        .catch_unwind()
        .await;
    context.pool.close().await;
    sqlx::query(&format!("DROP DATABASE {name} WITH (FORCE)"))
        .execute(&admin)
        .await?;
    admin.close().await;
    match result {
        Ok(result) => result,
        Err(payload) => std::panic::resume_unwind(payload),
    }
}

async fn exercise(context: &mut Context) -> Result<(), Box<dyn std::error::Error>> {
    let auth = Auth::new(Tokens::new(vec![1; 64])?);
    let session = auth
        .authenticate(
            context,
            Credentials {
                username: "persist_test".into(),
                password: "LocalTestOnly123!".into(),
                start: None,
            },
            true,
            1000,
        )
        .await?;
    let account = AccountId(session.account_id);
    let login = auth
        .authenticate(
            context,
            Credentials {
                username: "persist_test".into(),
                password: "LocalTestOnly123!".into(),
                start: None,
            },
            false,
            1000,
        )
        .await?;
    assert_eq!(login.account_id, session.account_id);
    assert!(auth
        .authenticate(
            context,
            Credentials {
                username: "persist_test".into(),
                password: "WrongPassword123!".into(),
                start: None
            },
            false,
            1000
        )
        .await
        .is_err());
    let before = context.command(account, &view(), 1000).await?;
    let Some(server_msg::Kind::World(snapshot)) = before.kind else {
        panic!("no world")
    };
    let ruler = snapshot.ruler.unwrap();
    let pos = ruler.pos.unwrap();
    let from = Position {
        lat: pos.lat,
        lon: pos.lon,
    };
    let source = world::shard(from);
    let mut destination = None;
    for x in -100..100 {
        for y in -100..100 {
            let to = Position {
                lat: from.lat + y as f64 / 50.0,
                lon: from.lon + x as f64 / 50.0,
            };
            if from.distance_m(to) < context.balance.ruler.max_trip_m
                && world::worker(world::shard(to)) != world::worker(source)
                && context
                    .geo
                    .route_is_land(from, to, context.balance.ruler.path_sample_m)
            {
                destination = Some(to);
                break;
            }
        }
        if destination.is_some() {
            break;
        }
    }
    let to = destination.expect("a land route across the local H3 worker boundary");
    let move_command = Command {
        request_id: "move-once".into(),
        kind: Some(command::Kind::MoveRuler(MoveRuler {
            destination: Some(LatLon {
                lat: to.lat,
                lon: to.lon,
            }),
        })),
    };
    accepted(context.command(account, &move_command, 1000).await?);
    accepted(context.command(account, &move_command, 1000).await?);
    let pending: i64 =
        sqlx::query_scalar("SELECT count(*) FROM scheduled_events WHERE done_at IS NULL")
            .fetch_one(&context.pool)
            .await?;
    assert_eq!(pending, 1);
    let pool = context.pool.clone(); // A new service instance reloads all state from PostgreSQL.
    *context = Context {
        pool,
        geo: context.geo.clone(),
        balance: context.balance.clone(),
        territory: context.territory.clone(),
    };
    assert!(!context.process_due(world::worker(source), 1001).await?);
    assert!(
        context
            .process_due(world::worker(source), 1_000_000)
            .await?
    );
    assert!(
        !context
            .process_due(world::worker(source), 1_000_000)
            .await?
    );
    let row = sqlx::query("SELECT shard,state FROM entities WHERE id=$1")
        .bind(Uuid::parse_str(&ruler.id)?)
        .fetch_one(&context.pool)
        .await?;
    let Json(arrived): Json<Ruler> = row.get("state");
    assert_eq!(arrived.pos, to);
    assert!(arrived.journey.is_none());
    let handoff = orbis_protocol::EntityHandoff {
        entity_id: ruler.id.clone(),
        revision: arrived.revision,
        from_shard: source,
        to_shard: world::shard(to),
        state: serde_json::to_vec(&arrived)?,
    };
    assert!(
        context
            .receive_handoff(&handoff, world::worker(handoff.to_shard))
            .await?
            .accepted
    );
    assert!(
        context
            .receive_handoff(&handoff, world::worker(handoff.to_shard))
            .await?
            .accepted
    );
    assert!(
        !context
            .receive_handoff(&handoff, world::worker(source))
            .await?
            .accepted
    );
    // Return to the spawn near water, then build and recover the village timer.
    let back = Command {
        request_id: "return".into(),
        kind: Some(command::Kind::MoveRuler(MoveRuler {
            destination: Some(pos),
        })),
    };
    accepted(context.command(account, &back, 1_000_001).await?);
    context
        .process_due(world::worker(world::shard(to)), 2_000_000)
        .await?;
    let found = Command {
        request_id: "first-village".into(),
        kind: Some(command::Kind::FoundCity(FoundCity {
            name: "Vila persistente".into(),
            pos: Some(LatLon {
                lat: from.lat,
                lon: from.lon,
            }),
        })),
    };
    accepted(context.command(account, &found, 2_000_000).await?);
    accepted(context.command(account, &found, 2_000_000).await?);
    assert!(
        context
            .process_due(world::worker(source), 3_000_000)
            .await?
    );
    assert!(
        !context
            .process_due(world::worker(source), 3_000_000)
            .await?
    );
    let Some(server_msg::Kind::World(snapshot)) =
        context.command(account, &view(), 3_000_000).await?.kind
    else {
        panic!("missing world")
    };
    assert_eq!(snapshot.cities.len(), 1);
    assert!(snapshot.cities[0].active);
    assert!(!snapshot.territory.is_empty());
    assert_eq!(
        snapshot.ruler.unwrap().supplies,
        context.balance.ruler.initial_supplies - context.balance.village.cost
    );
    let logs: i64 = sqlx::query_scalar("SELECT count(*) FROM event_log")
        .fetch_one(&context.pool)
        .await?;
    assert_eq!(logs, 3);
    Ok(())
}
