use orbis_core::{
    time::FixedClock,
    world::{AccountId, Action, City, CityId, Position, Ruler},
};
use orbis_protocol::{
    command, server_msg, CityState, Command, CommandResult, LatLon, MoveSegment, RulerState,
    ServerMsg, TerritoryCell, WorldSnapshot,
};
use orbis_sim::world::{self, Balance, GameError, Geography, TerritoryBalance, VillageOrder};
use prost::Message;
use sqlx::{types::Json, PgConnection, PgPool, Row};
use std::{collections::BTreeSet, sync::Arc};
use uuid::Uuid;

#[derive(Debug, thiserror::Error)]
pub enum ServiceError {
    #[error(transparent)]
    Database(#[from] sqlx::Error),
    #[error(transparent)]
    Json(#[from] serde_json::Error),
    #[error(transparent)]
    Io(#[from] std::io::Error),
    #[error(transparent)]
    Token(#[from] jsonwebtoken::errors::Error),
    #[error(transparent)]
    Config(#[from] toml::de::Error),
    #[error(transparent)]
    Game(#[from] GameError),
    #[error(transparent)]
    Wire(#[from] prost::DecodeError),
    #[error(transparent)]
    Migration(#[from] sqlx::migrate::MigrateError),
    #[error("unauthorized")]
    Unauthorized,
    #[error("{0}")]
    BadInput(&'static str),
    #[error("{0}")]
    Configuration(&'static str),
}
#[derive(Clone)]
pub struct Context {
    pub pool: PgPool,
    pub geo: Arc<Geography>,
    pub balance: Arc<Balance>,
    pub territory: Arc<TerritoryBalance>,
}
impl Context {
    pub async fn connect() -> Result<Self, ServiceError> {
        let url = std::env::var("DATABASE_URL").unwrap_or_else(|_| {
            "postgres://orbis:orbis@127.0.0.1:5432/orbis?sslmode=disable".into()
        });
        Self::connect_url(&url).await
    }
    pub async fn connect_url(url: &str) -> Result<Self, ServiceError> {
        let pool = sqlx::postgres::PgPoolOptions::new()
            .max_connections(12)
            .connect(url)
            .await?;
        sqlx::migrate!("../../infra/migrations").run(&pool).await?;
        let balance: Balance =
            toml::from_str(include_str!("../../../config/balance/first_village.toml"))?;
        balance.validate()?;
        let territory: TerritoryBalance =
            toml::from_str(include_str!("../../../config/balance/territory.toml"))?;
        territory.validate()?;
        let geo = Geography::from_json(
            include_str!("../../../apps/client/src/assets/world-land-v1.json"),
            include_str!("../../../apps/client/src/assets/world-terrain-v1.json"),
        )?;
        Ok(Self {
            pool,
            geo: Arc::new(geo),
            balance: Arc::new(balance),
            territory: Arc::new(territory),
        })
    }
    pub async fn actor_shard(&self, account: AccountId) -> Result<u64, ServiceError> {
        let shard:Option<i64>=sqlx::query_scalar("SELECT e.shard FROM entities e JOIN kingdoms k ON k.id=e.kingdom_id WHERE k.owner_account=$1 AND e.kind=0").bind(account.0).fetch_optional(&self.pool).await?;
        shard.map(|id| id as u64).ok_or(ServiceError::Unauthorized)
    }
    pub async fn command(
        &self,
        account: AccountId,
        command: &Command,
        now_ms: u64,
    ) -> Result<ServerMsg, ServiceError> {
        if command.request_id.is_empty() || command.request_id.len() > 128 {
            return Err(ServiceError::BadInput("invalid_request_id"));
        }
        let mut tx = self.pool.begin().await?;
        let founding = matches!(command.kind, Some(command::Kind::FoundCity(_)));
        if founding {
            foundation_lock(&mut tx).await?;
        }
        let mut ruler = load_ruler(&mut tx, account).await?;
        if matches!(command.kind, Some(command::Kind::Viewport(_))) {
            if let Some(command::Kind::Viewport(view)) = &command.kind {
                orbis_sim::demo::snapshot(view, 0)
                    .map_err(|_| ServiceError::BadInput("invalid_viewport"))?;
            }
            let cities = load_cities(&mut tx).await?;
            let rows=sqlx::query("SELECT t.h3_res7,t.city_id FROM territory_cells t JOIN cities c ON c.id=t.city_id WHERE c.kingdom_id=$1").bind(ruler.kingdom.0).fetch_all(&mut *tx).await?;
            let cells = rows
                .into_iter()
                .map(|row| {
                    let cell: i64 = row.get("h3_res7");
                    let city: Uuid = row.get("city_id");
                    TerritoryCell {
                        h3: format!("{:x}", cell),
                        city_id: city.to_string(),
                        boundary: world::boundary(cell as u64)
                            .into_iter()
                            .map(latlon)
                            .collect(),
                    }
                })
                .collect();
            return Ok(ServerMsg {
                kind: Some(server_msg::Kind::World(WorldSnapshot {
                    ruler: Some(ruler_state(&ruler)),
                    cities: cities.iter().map(city_state).collect(),
                    territory: cells,
                    now_ms: now_ms as f64,
                    village_cost: self.balance.village.cost,
                    village_construction_ms: self.balance.village.construction_ms as f64,
                })),
            });
        }
        if let Some(bytes) = sqlx::query_scalar::<_, Vec<u8>>(
            "SELECT response FROM command_receipts WHERE account_id=$1 AND request_id=$2",
        )
        .bind(account.0)
        .bind(&command.request_id)
        .fetch_optional(&mut *tx)
        .await?
        {
            return Ok(ServerMsg::decode(bytes.as_slice())?);
        }
        let clock = FixedClock(now_ms);
        let planned = match &command.kind {
            Some(command::Kind::MoveRuler(order)) => {
                let pos = order
                    .destination
                    .as_ref()
                    .map(|p| Position {
                        lat: p.lat,
                        lon: p.lon,
                    })
                    .ok_or(GameError("destination_required"));
                pos.and_then(|p| world::plan_move(&ruler, p, &clock, &self.geo, &self.balance))
                    .map(|(next, action, due)| (next, None, action, due))
            }
            Some(command::Kind::FoundCity(order)) => {
                let cities = load_cities(&mut tx).await?;
                let pos = order
                    .pos
                    .as_ref()
                    .map(|p| Position {
                        lat: p.lat,
                        lon: p.lon,
                    })
                    .ok_or(GameError("position_required"));
                pos.and_then(|pos| {
                    world::plan_village(
                        &ruler,
                        VillageOrder {
                            id: CityId(Uuid::new_v4()),
                            name: &order.name,
                            pos,
                        },
                        &cities,
                        &clock,
                        &self.geo,
                        &self.balance,
                    )
                })
                .map(|(next, city, action)| {
                    let due = city.ready_at_ms;
                    (next, Some(city), action, due)
                })
            }
            _ => Err(GameError("command_not_available")),
        };
        let response = match planned {
            Ok((next, city, action, due)) => {
                ruler = next;
                save_ruler(&mut tx, &ruler).await?;
                if let Some(city) = city {
                    save_city(&mut tx, &city).await?;
                }
                let shard = world::shard(ruler.pos);
                sqlx::query("INSERT INTO scheduled_events(id,shard,due_at,kind,payload,idempotency_key,worker_slot) VALUES($1,$2,to_timestamp($3),0,$4,$5,$6)")
                    .bind(Uuid::new_v4()).bind(shard as i64).bind(due as f64/1000.0).bind(Json(&action)).bind(format!("{}:{}",account.0,command.request_id)).bind(world::worker(shard) as i16).execute(&mut *tx).await?;
                result(&command.request_id, true, "")
            }
            Err(error) => result(&command.request_id, false, error.0),
        };
        sqlx::query(
            "INSERT INTO command_receipts(account_id,request_id,response) VALUES($1,$2,$3)",
        )
        .bind(account.0)
        .bind(&command.request_id)
        .bind(response.encode_to_vec())
        .execute(&mut *tx)
        .await?;
        tx.commit().await?;
        Ok(response)
    }
    /// Consume indexed due events only. Effects and done_at commit together.
    pub async fn process_due(&self, slot: u16, now_ms: u64) -> Result<bool, ServiceError> {
        let mut tx = self.pool.begin().await?;
        let row=sqlx::query("SELECT id,payload,shard FROM scheduled_events WHERE worker_slot=$1 AND done_at IS NULL AND due_at<=to_timestamp($2) ORDER BY due_at,id FOR UPDATE SKIP LOCKED LIMIT 1")
            .bind(slot as i16).bind(now_ms as f64/1000.0).fetch_optional(&mut *tx).await?;
        let Some(row) = row else { return Ok(false) };
        let event_id: Uuid = row.get("id");
        let action: Json<Action> = row.get("payload");
        let old_shard: i64 = row.get("shard");
        if matches!(action.0, Action::CompleteVillage { .. }) {
            foundation_lock(&mut tx).await?;
        }
        let ruler_id = match action.0 {
            Action::Arrive { ruler, .. } | Action::CompleteVillage { ruler, .. } => ruler,
        };
        let Json(ruler): Json<Ruler> =
            sqlx::query_scalar("SELECT state FROM entities WHERE id=$1 FOR UPDATE")
                .bind(ruler_id.0)
                .fetch_one(&mut *tx)
                .await?;
        let city = match &action.0 {
            Action::CompleteVillage { city, .. } => {
                sqlx::query_scalar::<_, Json<City>>("SELECT state FROM cities WHERE id=$1")
                    .bind(city.0)
                    .fetch_optional(&mut *tx)
                    .await?
                    .map(|Json(city)| city)
            }
            _ => None,
        };
        let (next, town) = world::complete(&ruler, city.as_ref(), &action.0, &FixedClock(now_ms));
        if next != ruler {
            save_ruler(&mut tx, &next).await?;
            if let Some(town) = town {
                save_city(&mut tx, &town).await?;
                let affected: BTreeSet<_> = world::footprint(&town, &self.territory, &self.geo)
                    .into_iter()
                    .map(|(id, _)| id)
                    .collect();
                let cities = load_cities(&mut tx).await?;
                let owners = world::territory(&cities, &self.territory, &self.geo, Some(&affected));
                if !owners.is_empty() {
                    let mut query = sqlx::QueryBuilder::<sqlx::Postgres>::new(
                        "INSERT INTO territory_cells(h3_res7,city_id,influence) ",
                    );
                    query.push_values(owners.iter(), |mut row, (cell, (owner, influence))| {
                        row.push_bind(*cell as i64)
                            .push_bind(owner.0)
                            .push_bind(*influence);
                    });
                    query.push(" ON CONFLICT(h3_res7) DO UPDATE SET city_id=EXCLUDED.city_id,influence=EXCLUDED.influence");
                    query.build().execute(&mut *tx).await?;
                }
            }
            let new_shard = world::shard(next.pos);
            if new_shard != old_shard as u64 {
                sqlx::query("INSERT INTO entity_handoffs(entity_id,revision,from_shard,to_shard,source_worker,state) VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT DO NOTHING")
                    .bind(next.id.0).bind(next.revision as i64).bind(old_shard).bind(new_shard as i64).bind(slot as i16).bind(serde_json::to_vec(&next)?).execute(&mut *tx).await?;
            }
            sqlx::query("INSERT INTO event_log(shard,kind,payload) VALUES($1,0,$2)")
                .bind(old_shard)
                .bind(Json(&action.0))
                .execute(&mut *tx)
                .await?;
        }
        sqlx::query("UPDATE scheduled_events SET done_at=now() WHERE id=$1")
            .bind(event_id)
            .execute(&mut *tx)
            .await?;
        tx.commit().await?;
        Ok(true)
    }
}

async fn foundation_lock(conn: &mut PgConnection) -> Result<(), sqlx::Error> {
    sqlx::query("SELECT pg_advisory_xact_lock(1729481)")
        .execute(conn)
        .await?;
    Ok(())
}
async fn load_ruler(conn: &mut PgConnection, account: AccountId) -> Result<Ruler, ServiceError> {
    let value=sqlx::query_scalar::<_,Json<Ruler>>("SELECT e.state FROM entities e JOIN kingdoms k ON k.id=e.kingdom_id WHERE k.owner_account=$1 AND e.kind=0 FOR UPDATE OF e").bind(account.0).fetch_optional(conn).await?.ok_or(ServiceError::Unauthorized)?;
    Ok(value.0)
}
async fn load_cities(conn: &mut PgConnection) -> Result<Vec<City>, sqlx::Error> {
    Ok(sqlx::query_scalar::<_, Json<City>>(
        "SELECT state FROM cities WHERE state IS NOT NULL ORDER BY id",
    )
    .fetch_all(conn)
    .await?
    .into_iter()
    .map(|Json(city)| city)
    .collect())
}
async fn save_ruler(conn: &mut PgConnection, ruler: &Ruler) -> Result<(), sqlx::Error> {
    sqlx::query("UPDATE entities SET state=$2,shard=$3 WHERE id=$1")
        .bind(ruler.id.0)
        .bind(Json(ruler))
        .bind(world::shard(ruler.pos) as i64)
        .execute(conn)
        .await?;
    Ok(())
}
async fn save_city(conn: &mut PgConnection, city: &City) -> Result<(), sqlx::Error> {
    sqlx::query("INSERT INTO cities(id,kingdom_id,name,pos,h3_shard,tier,population,state) VALUES($1,$2,$3,ST_SetSRID(ST_MakePoint($4,$5),4326)::geography,$6,$7,$8,$9) ON CONFLICT(id) DO UPDATE SET state=EXCLUDED.state,population=EXCLUDED.population")
        .bind(city.id.0).bind(city.kingdom.0).bind(&city.name).bind(city.pos.lon).bind(city.pos.lat).bind(world::shard(city.pos) as i64).bind(city.tier as i16).bind(serde_json::json!({"total":city.population})).bind(Json(city)).execute(conn).await?;
    Ok(())
}
pub fn result(request: &str, ok: bool, error: &str) -> ServerMsg {
    ServerMsg {
        kind: Some(server_msg::Kind::Result(CommandResult {
            request_id: request.into(),
            ok,
            error: error.into(),
        })),
    }
}
pub fn latlon(pos: Position) -> LatLon {
    LatLon {
        lat: pos.lat,
        lon: pos.lon,
    }
}
fn ruler_state(ruler: &Ruler) -> RulerState {
    RulerState {
        id: ruler.id.0.to_string(),
        kingdom_id: ruler.kingdom.0.to_string(),
        pos: Some(latlon(ruler.pos)),
        supplies: ruler.supplies,
        journey: ruler.journey.as_ref().map(|j| MoveSegment {
            entity_id: ruler.id.0.to_string(),
            from: Some(latlon(j.from)),
            to: Some(latlon(j.to)),
            t_start_ms: j.start_ms as f64,
            speed_mps: j.from.distance_m(j.to) / ((j.due_ms - j.start_ms) as f64 / 1000.0),
            path_id: ruler.revision.to_string(),
        }),
        journey_due_ms: ruler.journey.as_ref().map_or(0.0, |j| j.due_ms as f64),
        founding_until_ms: ruler.founding_until.unwrap_or(0) as f64,
        city_id: ruler.city.map_or_else(String::new, |id| id.0.to_string()),
        revision: ruler.revision,
    }
}
fn city_state(city: &City) -> CityState {
    CityState {
        id: city.id.0.to_string(),
        kingdom_id: city.kingdom.0.to_string(),
        name: city.name.clone(),
        pos: Some(latlon(city.pos)),
        tier: city.tier as u32,
        active: city.active,
        ready_at_ms: city.ready_at_ms as f64,
        population: city.population,
    }
}
