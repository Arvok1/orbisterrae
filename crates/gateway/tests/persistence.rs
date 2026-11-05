use futures_util::FutureExt;
use orbis_core::world::{AccountId, Position, Ruler};
use orbis_gateway::{
    auth::{Auth, Credentials, Tokens},
    world_service::Context,
};
use orbis_protocol::{
    command, server_msg, ApplyCityEdits, CityCellEdit, Command, FoundCity, InspectCity, LatLon,
    MoveRuler, QueueBuilding, SetViewport,
};
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
        city_catalog: context.city_catalog.clone(),
        city_balance: context.city_balance.clone(),
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
    let city_id = snapshot.cities[0].id.clone();
    let inspect = Command {
        request_id: "inspect-city".into(),
        kind: Some(command::Kind::InspectCity(InspectCity {
            city_id: city_id.clone(),
        })),
    };
    let Some(server_msg::Kind::City(initial_city)) =
        context.command(account, &inspect, 3_000_000).await?.kind
    else {
        panic!("missing city snapshot")
    };
    assert!(initial_city.editable);
    assert_eq!(initial_city.width, 96);
    assert_eq!(initial_city.buildings.len(), 3);
    let edits = (10..12)
        .flat_map(|y| {
            (10..12).map(move |x| CityCellEdit {
                x,
                y,
                layer: "zone".into(),
                value: "residential".into(),
            })
        })
        .collect();
    let stale = Command {
        request_id: "stale-layout".into(),
        kind: Some(command::Kind::ApplyCityEdits(ApplyCityEdits {
            city_id: city_id.clone(),
            expected_revision: initial_city.revision - 1,
            edits: Vec::new(),
        })),
    };
    let Some(server_msg::Kind::Result(stale_result)) =
        context.command(account, &stale, 3_000_001).await?.kind
    else {
        panic!("missing stale revision result")
    };
    assert!(!stale_result.ok);
    assert_eq!(stale_result.error, "stale_city_revision");
    let edit = Command {
        request_id: "edit-layout".into(),
        kind: Some(command::Kind::ApplyCityEdits(ApplyCityEdits {
            city_id: city_id.clone(),
            expected_revision: initial_city.revision,
            edits,
        })),
    };
    let Some(server_msg::Kind::City(edited)) =
        context.command(account, &edit, 3_000_002).await?.kind
    else {
        panic!("missing edited city")
    };
    let queue = Command {
        request_id: "queue-house".into(),
        kind: Some(command::Kind::QueueBuilding(QueueBuilding {
            city_id: city_id.clone(),
            expected_revision: edited.revision,
            building_id: "house".into(),
            x: 10,
            y: 10,
            orientation: 0,
        })),
    };
    let Some(server_msg::Kind::City(queued)) =
        context.command(account, &queue, 3_000_003).await?.kind
    else {
        panic!("missing queued city")
    };
    assert_eq!(queued.construction[0].status, "active");
    let pool = context.pool.clone();
    *context = Context {
        pool,
        geo: context.geo.clone(),
        balance: context.balance.clone(),
        territory: context.territory.clone(),
        city_catalog: context.city_catalog.clone(),
        city_balance: context.city_balance.clone(),
    };
    assert!(context.process_due(world::worker(source), 3_006_000).await?);
    assert!(!context.process_due(world::worker(source), 3_006_000).await?);
    let Some(server_msg::Kind::City(completed)) =
        context.command(account, &inspect, 3_006_000).await?.kind
    else {
        panic!("missing completed city")
    };
    assert!(completed.buildings.iter().any(|building| {
        building.building_id == "house" && building.x == 10 && building.y == 10 && building.complete
    }));
    let observer = auth
        .authenticate(
            context,
            Credentials {
                username: "city_observer".into(),
                password: "LocalTestOnly123!".into(),
                start: None,
            },
            true,
            3_006_000,
        )
        .await?;
    let observer = AccountId(observer.account_id);
    let Some(server_msg::Kind::City(read_only)) =
        context.command(observer, &inspect, 3_006_001).await?.kind
    else {
        panic!("observer did not receive city")
    };
    assert!(!read_only.editable);
    let forbidden = Command {
        request_id: "observer-edit".into(),
        kind: Some(command::Kind::ApplyCityEdits(ApplyCityEdits {
            city_id,
            expected_revision: read_only.revision,
            edits: Vec::new(),
        })),
    };
    let Some(server_msg::Kind::Result(forbidden)) =
        context.command(observer, &forbidden, 3_006_002).await?.kind
    else {
        panic!("missing read-only result")
    };
    assert_eq!(forbidden.error, "city_read_only");
    let logs: i64 = sqlx::query_scalar("SELECT count(*) FROM event_log")
        .fetch_one(&context.pool)
        .await?;
    assert_eq!(logs, 4);
    Ok(())
}
