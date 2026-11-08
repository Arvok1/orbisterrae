use orbis_core::{
    city::{CityLayout, Construction, ConstructionId},
    economy::{Hub, Stock},
    time::FixedClock,
    world::{
        AccountId, Action, CaravanId, City, CityId, HubId, KingdomId, Position, Ruler, ShipId,
    },
};
use orbis_protocol::{
    command, server_msg, AdminEvent, AdminSnapshot, AdminSnapshotRequest,
    BuildingDefinition as WireBuildingDefinition, BuildingState, CityEventState, CityMetrics,
    CitySnapshot, CityState, Command, CommandResult, ConstructionState, HubState, HubsState,
    LatLon, MarketSnapshot, MoveSegment, OrderState, PopulationState, RulerState, ServerMsg,
    TerritoryCell, WorldSnapshot,
};
use orbis_sim::city::{self, Catalog as CityCatalog};
use orbis_sim::economy::{Balance as EconomyBalance, Catalog as EconomyCatalog, EconomyError};
use orbis_sim::world::{self, Balance, GameError, Geography, TerritoryBalance, VillageOrder};
use prost::Message;
use sqlx::{types::Json, PgConnection, PgPool, QueryBuilder, Row};
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
    City(#[from] city::CityError),
    #[error("{0}")]
    Economy(#[from] EconomyError),
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
    pub city_catalog: Arc<CityCatalog>,
    pub city_balance: Arc<city::Balance>,
    pub economy_catalog: Arc<EconomyCatalog>,
    pub economy_balance: Arc<EconomyBalance>,
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
        let city_catalog = CityCatalog::parse(include_str!("../../../data/buildings/mvp.toml"))?;
        city_catalog.validate()?;
        let mut city_balance =
            city::Balance::parse(include_str!("../../../config/balance/city.toml"))?;
        // Test/harness only: the bot scenario accelerates city ticks without touching
        // production config. See docs/TESTING.md.
        if let Ok(raw) = std::env::var("ORBIS_CITY_TICK_MS") {
            let millis: u64 = raw
                .trim()
                .parse()
                .map_err(|_| ServiceError::Configuration("invalid ORBIS_CITY_TICK_MS"))?;
            if millis == 0 {
                return Err(ServiceError::Configuration(
                    "ORBIS_CITY_TICK_MS must be positive",
                ));
            }
            city_balance.simulation.tick_ms = millis;
        }
        city_balance.validate()?;
        let economy_catalog =
            EconomyCatalog::parse(include_str!("../../../data/economy/commodities.toml"))?;
        economy_catalog.validate()?;
        let economy_balance: EconomyBalance =
            toml::from_str(include_str!("../../../config/balance/economy.toml"))?;
        economy_balance.validate()?;
        let geo = Geography::from_json(
            include_str!("../../../apps/client/src/assets/world-land-v1.json"),
            include_str!("../../../apps/client/src/assets/world-terrain-v1.json"),
        )?;
        let context = Self {
            pool,
            geo: Arc::new(geo),
            balance: Arc::new(balance),
            territory: Arc::new(territory),
            city_catalog: Arc::new(city_catalog),
            city_balance: Arc::new(city_balance),
            economy_catalog: Arc::new(economy_catalog),
            economy_balance: Arc::new(economy_balance),
        };
        context.seed_hubs().await?;
        Ok(context)
    }
    /// Creates the IA hubs once per world, deterministically from the seeded balance.
    async fn seed_hubs(&self) -> Result<(), ServiceError> {
        let mut tx = self.pool.begin().await?;
        let existing: i64 = sqlx::query_scalar("SELECT COUNT(*) FROM hubs")
            .fetch_one(&mut *tx)
            .await?;
        if existing > 0 {
            tx.commit().await?;
            return Ok(());
        }
        for hub in orbis_sim::economy::seeded_hubs(
            &self.geo,
            self.economy_balance.hub_seed.seed,
            &self.economy_balance,
            &self.economy_catalog,
        ) {
            sqlx::query(
                "INSERT INTO hubs(id,name,pos,h3_shard,kingdom_id,stock,metal,fee) \
                 VALUES($1,$2,ST_SetSRID(ST_MakePoint($3,$4),4326)::geography,$5,NULL,$6,$7,$8) \
                 ON CONFLICT (id) DO NOTHING",
            )
            .bind(hub.id.0)
            .bind(&hub.name)
            .bind(hub.pos.lon)
            .bind(hub.pos.lat)
            .bind(world::shard(hub.pos) as i64)
            .bind(Json(&hub.stock))
            .bind(hub.metal as i64)
            .bind(hub.fee)
            .execute(&mut *tx)
            .await?;
        }
        tx.commit().await?;
        Ok(())
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
            let has_res8: bool =
                sqlx::query_scalar("SELECT EXISTS(SELECT 1 FROM territory_cells_res8)")
                    .fetch_one(&mut *tx)
                    .await?;
            if !has_res8 {
                let ownership = world::territory(&cities, &self.territory, &self.geo, None);
                for (cell, (city, influence)) in ownership {
                    sqlx::query("INSERT INTO territory_cells_res8(h3_res8,city_id,influence) VALUES($1,$2,$3) ON CONFLICT(h3_res8) DO UPDATE SET city_id=EXCLUDED.city_id,influence=EXCLUDED.influence")
                        .bind(cell as i64).bind(city.0).bind(influence).execute(&mut *tx).await?;
                }
            }
            let rows=sqlx::query("SELECT t.h3_res8,t.city_id FROM territory_cells_res8 t JOIN cities c ON c.id=t.city_id WHERE c.kingdom_id=$1").bind(ruler.kingdom.0).fetch_all(&mut *tx).await?;
            let cells = rows
                .into_iter()
                .map(|row| {
                    let cell: i64 = row.get("h3_res8");
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
                    cities: cities
                        .iter()
                        .filter(|city| city.kingdom == ruler.kingdom)
                        .map(city_state)
                        .collect(),
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
        if matches!(
            command.kind,
            Some(command::Kind::InspectCity(_))
                | Some(command::Kind::ApplyCityEdits(_))
                | Some(command::Kind::QueueBuilding(_))
                | Some(command::Kind::CancelConstruction(_))
        ) {
            let cache_response = !matches!(command.kind, Some(command::Kind::InspectCity(_)));
            let response = self.city_command(&mut tx, &ruler, command, now_ms).await?;
            if cache_response {
                sqlx::query(
                    "INSERT INTO command_receipts(account_id,request_id,response) VALUES($1,$2,$3)",
                )
                .bind(account.0)
                .bind(&command.request_id)
                .bind(response.encode_to_vec())
                .execute(&mut *tx)
                .await?;
            }
            tx.commit().await?;
            return Ok(response);
        }
        if matches!(
            command.kind,
            Some(command::Kind::CreateCaravan(_))
                | Some(command::Kind::CreateShip(_))
                | Some(command::Kind::PlaceOrder(_))
                | Some(command::Kind::CancelOrder(_))
                | Some(command::Kind::GetMarket(_))
                | Some(command::Kind::GetHub(_))
                | Some(command::Kind::ListHubs(_))
        ) {
            let response = match &command.kind {
                Some(command::Kind::CreateCaravan(order)) => {
                    self.economy_create_caravan(&mut tx, &ruler, order, &command.request_id, now_ms)
                        .await?
                }
                Some(command::Kind::CreateShip(order)) => {
                    self.economy_create_ship(&mut tx, &ruler, order, &command.request_id, now_ms)
                        .await?
                }
                Some(command::Kind::PlaceOrder(order)) => {
                    self.economy_place_order(&mut tx, &ruler, order, &command.request_id, now_ms)
                        .await?
                }
                Some(command::Kind::CancelOrder(order)) => {
                    self.economy_cancel_order(&mut tx, &ruler, order, &command.request_id, now_ms)
                        .await?
                }
                Some(command::Kind::GetMarket(order)) => {
                    self.economy_get_market(&mut tx, &ruler, order, &command.request_id, now_ms)
                        .await?
                }
                Some(command::Kind::GetHub(order)) => {
                    self.economy_get_hub(&mut tx, &ruler, order, &command.request_id, now_ms)
                        .await?
                }
                Some(command::Kind::ListHubs(order)) => {
                    self.economy_list_hubs(&mut tx, &ruler, order, &command.request_id, now_ms)
                        .await?
                }
                _ => unreachable!("kind checked by matches! above"),
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
            return Ok(response);
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

    pub async fn admin_snapshot(
        &self,
        admin: AccountId,
        request: &AdminSnapshotRequest,
        now_ms: u64,
    ) -> Result<ServerMsg, ServiceError> {
        let mut tx = self.pool.begin().await?;
        let role: Option<String> =
            sqlx::query_scalar("SELECT role FROM accounts WHERE id=$1 FOR SHARE")
                .bind(admin.0)
                .fetch_optional(&mut *tx)
                .await?;
        if role.as_deref() != Some("admin") {
            return Err(ServiceError::Unauthorized);
        }
        let filters = serde_json::json!({
            "afterEventSeq": request.after_event_seq.to_string(), "accountId": request.account_id,
            "kingdomId": request.kingdom_id, "cityId": request.city_id,
            "shard": request.shard, "eventKind": request.event_kind,
            "filterShard": request.filter_shard, "filterEventKind": request.filter_event_kind,
        });
        sqlx::query(
            "INSERT INTO admin_audit(account_id,action,filters) VALUES($1,'world_snapshot',$2)",
        )
        .bind(admin.0)
        .bind(Json(filters))
        .execute(&mut *tx)
        .await?;

        let city_rows = if request.include_world {
            sqlx::query("SELECT state FROM cities WHERE state IS NOT NULL ORDER BY founded_at,id")
                .fetch_all(&mut *tx)
                .await?
        } else {
            Vec::new()
        };
        let cities: Vec<City> = city_rows
            .into_iter()
            .filter_map(|row| {
                row.try_get::<Json<City>, _>("state")
                    .ok()
                    .map(|Json(city)| city)
            })
            .collect();
        if request.include_world {
            let has_res8: bool =
                sqlx::query_scalar("SELECT EXISTS(SELECT 1 FROM territory_cells_res8)")
                    .fetch_one(&mut *tx)
                    .await?;
            if !has_res8 {
                for (cell, (city, influence)) in
                    world::territory(&cities, &self.territory, &self.geo, None)
                {
                    sqlx::query("INSERT INTO territory_cells_res8(h3_res8,city_id,influence) VALUES($1,$2,$3) ON CONFLICT(h3_res8) DO UPDATE SET city_id=EXCLUDED.city_id,influence=EXCLUDED.influence")
                        .bind(cell as i64).bind(city.0).bind(influence).execute(&mut *tx).await?;
                }
            }
        }
        let cities: Vec<CityState> = cities.iter().map(city_state).collect();
        let entity_rows = if request.include_world {
            sqlx::query("SELECT id,kind,kingdom_id,state FROM entities ORDER BY id")
                .fetch_all(&mut *tx)
                .await?
        } else {
            Vec::new()
        };
        let entities = entity_rows
            .into_iter()
            .filter_map(|row| {
                let state: Json<serde_json::Value> = row.try_get("state").ok()?;
                let kind: i16 = row.try_get("kind").ok()?;
                let (lat, lon) = if kind == 0 {
                    let ruler: Ruler = serde_json::from_value(state.0.clone()).ok()?;
                    let position = ruler
                        .journey
                        .as_ref()
                        .map(|journey| journey.position(now_ms))
                        .unwrap_or(ruler.pos);
                    (position.lat, position.lon)
                } else {
                    let position = state.0.get("pos")?;
                    (
                        position.get("lat")?.as_f64()?,
                        position.get("lon")?.as_f64()?,
                    )
                };
                Some(orbis_protocol::EntityUpsert {
                    id: row.try_get::<Uuid, _>("id").ok()?.to_string(),
                    kind: kind as u32,
                    pos: Some(LatLon { lat, lon }),
                    kingdom_id: row.try_get::<Uuid, _>("kingdom_id").ok()?.to_string(),
                })
            })
            .collect();
        let territory_rows = if request.include_world {
            sqlx::query("SELECT h3_res8,city_id FROM territory_cells_res8 ORDER BY h3_res8")
                .fetch_all(&mut *tx)
                .await?
        } else {
            Vec::new()
        };
        let territory = territory_rows
            .into_iter()
            .filter_map(|row| {
                let cell = row.try_get::<i64, _>("h3_res8").ok()? as u64;
                Some(TerritoryCell {
                    h3: format!("{cell:x}"),
                    city_id: row.try_get::<Uuid, _>("city_id").ok()?.to_string(),
                    boundary: world::boundary(cell).into_iter().map(latlon).collect(),
                })
            })
            .collect();

        let mut query = QueryBuilder::<sqlx::Postgres>::new("SELECT e.seq,(extract(epoch from e.at)::float8)*1000 AS at_ms,e.shard,e.kind,e.payload,e.account_id::text AS account_id,e.kingdom_id::text AS kingdom_id,e.city_id::text AS city_id,CASE WHEN c.id IS NULL THEN '' ELSE concat(round(ST_Y(c.pos::geometry)::numeric,2),', ',round(ST_X(c.pos::geometry)::numeric,2)) END AS region FROM event_log e LEFT JOIN cities c ON c.id=e.city_id WHERE e.seq > ");
        query.push_bind(request.after_event_seq as i64);
        if !request.account_id.is_empty() {
            query.push(" AND e.account_id::text = ");
            query.push_bind(&request.account_id);
        }
        if !request.kingdom_id.is_empty() {
            query.push(" AND e.kingdom_id::text = ");
            query.push_bind(&request.kingdom_id);
        }
        if !request.city_id.is_empty() {
            query.push(" AND e.city_id::text = ");
            query.push_bind(&request.city_id);
        }
        if request.filter_shard {
            query.push(" AND e.shard = ");
            query.push_bind(request.shard as i64);
        }
        if request.filter_event_kind {
            query.push(" AND e.kind = ");
            query.push_bind(request.event_kind as i16);
        }
        if request.after_event_seq == 0 {
            query.push(" ORDER BY e.seq DESC LIMIT 200");
        } else {
            query.push(" ORDER BY e.seq LIMIT 200");
        }
        let event_rows = query.build().fetch_all(&mut *tx).await?;
        let mut next_event_seq = request.after_event_seq;
        let mut events: Vec<AdminEvent> = event_rows
            .into_iter()
            .filter_map(|row| {
                let seq = row.try_get::<i64, _>("seq").ok()? as u64;
                next_event_seq = next_event_seq.max(seq);
                let payload: Json<serde_json::Value> = row.try_get("payload").ok()?;
                let at_ms: f64 = row.try_get("at_ms").ok()?;
                Some(AdminEvent {
                    seq,
                    at_ms,
                    shard: row.try_get::<i64, _>("shard").ok()? as u64,
                    kind: row.try_get::<i16, _>("kind").ok()? as u32,
                    region: row.try_get::<String, _>("region").unwrap_or_default(),
                    account_id: row
                        .try_get::<Option<String>, _>("account_id")
                        .ok()
                        .flatten()
                        .unwrap_or_default(),
                    kingdom_id: row
                        .try_get::<Option<String>, _>("kingdom_id")
                        .ok()
                        .flatten()
                        .unwrap_or_default(),
                    city_id: row
                        .try_get::<Option<String>, _>("city_id")
                        .ok()
                        .flatten()
                        .unwrap_or_default(),
                    payload_json: payload.0.to_string(),
                })
            })
            .collect();
        if request.after_event_seq == 0 {
            events.reverse();
        }
        if events.len() < 200 {
            let latest: i64 = sqlx::query_scalar("SELECT COALESCE(MAX(seq),0) FROM event_log")
                .fetch_one(&mut *tx)
                .await?;
            next_event_seq = next_event_seq.max(latest as u64);
        }
        tx.commit().await?;
        Ok(ServerMsg {
            kind: Some(server_msg::Kind::Admin(AdminSnapshot {
                cities,
                entities,
                events,
                territory,
                next_event_seq,
                generated_at_ms: now_ms as f64,
            })),
        })
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
        if matches!(
            action.0,
            Action::CompleteConstruction { .. } | Action::CityTick { .. }
        ) {
            self.process_city_action(&mut tx, &action.0, now_ms).await?;
            let city_id = match &action.0 {
                Action::CompleteConstruction { city, .. } | Action::CityTick { city, .. } => city.0,
                _ => unreachable!(),
            };
            sqlx::query("INSERT INTO event_log(shard,kind,payload,account_id,kingdom_id,city_id) SELECT $1,1,$2,k.owner_account,c.kingdom_id,c.id FROM cities c JOIN kingdoms k ON k.id=c.kingdom_id WHERE c.id=$3")
                .bind(old_shard).bind(Json(&action.0)).bind(city_id).execute(&mut *tx).await?;
            sqlx::query("UPDATE scheduled_events SET done_at=now() WHERE id=$1")
                .bind(event_id)
                .execute(&mut *tx)
                .await?;
            tx.commit().await?;
            return Ok(true);
        }
        if matches!(
            action.0,
            Action::CompleteCaravan { .. } | Action::CompleteShip { .. }
        ) {
            // The arrival session logs its own event; the deferred event just completes.
            self.process_economy_action(&mut tx, &action.0, now_ms)
                .await?;
            sqlx::query("UPDATE scheduled_events SET done_at=now() WHERE id=$1")
                .bind(event_id)
                .execute(&mut *tx)
                .await?;
            tx.commit().await?;
            return Ok(true);
        }
        if matches!(action.0, Action::CompleteVillage { .. }) {
            foundation_lock(&mut tx).await?;
        }
        let ruler_id = match action.0 {
            Action::Arrive { ruler, .. } | Action::CompleteVillage { ruler, .. } => ruler,
            Action::CompleteConstruction { .. }
            | Action::CityTick { .. }
            | Action::CompleteCaravan { .. }
            | Action::CompleteShip { .. } => unreachable!(),
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
                        "INSERT INTO territory_cells_res8(h3_res8,city_id,influence) ",
                    );
                    query.push_values(owners.iter(), |mut row, (cell, (owner, influence))| {
                        row.push_bind(*cell as i64)
                            .push_bind(owner.0)
                            .push_bind(*influence);
                    });
                    query.push(" ON CONFLICT(h3_res8) DO UPDATE SET city_id=EXCLUDED.city_id,influence=EXCLUDED.influence");
                    query.build().execute(&mut *tx).await?;
                }
            }
            let new_shard = world::shard(next.pos);
            if new_shard != old_shard as u64 {
                sqlx::query("INSERT INTO entity_handoffs(entity_id,revision,from_shard,to_shard,source_worker,state) VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT DO NOTHING")
                    .bind(next.id.0).bind(next.revision as i64).bind(old_shard).bind(new_shard as i64).bind(slot as i16).bind(serde_json::to_vec(&next)?).execute(&mut *tx).await?;
            }
            let city_id = match &action.0 {
                Action::CompleteVillage { city, .. } => Some(city.0),
                _ => None,
            };
            sqlx::query("INSERT INTO event_log(shard,kind,payload,account_id,kingdom_id,city_id) SELECT $1,0,$2,k.owner_account,k.id,$4 FROM kingdoms k JOIN entities e ON e.kingdom_id=k.id WHERE e.id=$3")
                .bind(old_shard).bind(Json(&action.0)).bind(next.id.0).bind(city_id).execute(&mut *tx).await?;
        }
        sqlx::query("UPDATE scheduled_events SET done_at=now() WHERE id=$1")
            .bind(event_id)
            .execute(&mut *tx)
            .await?;
        tx.commit().await?;
        Ok(true)
    }

    async fn city_command(
        &self,
        conn: &mut PgConnection,
        ruler: &Ruler,
        command: &Command,
        now_ms: u64,
    ) -> Result<ServerMsg, ServiceError> {
        let raw_city_id = match command.kind.as_ref() {
            Some(command::Kind::InspectCity(order)) => &order.city_id,
            Some(command::Kind::ApplyCityEdits(order)) => &order.city_id,
            Some(command::Kind::QueueBuilding(order)) => &order.city_id,
            Some(command::Kind::CancelConstruction(order)) => &order.city_id,
            _ => return Ok(result(&command.request_id, false, "command_not_available")),
        };
        let Ok(city_id) = Uuid::parse_str(raw_city_id) else {
            return Ok(result(&command.request_id, false, "invalid_city_id"));
        };
        let Some(town) = load_city(conn, CityId(city_id), true).await? else {
            return Ok(result(&command.request_id, false, "city_not_found"));
        };
        let mut layout =
            ensure_layout(conn, &town, now_ms, &self.city_catalog, &self.city_balance).await?;
        let mut orders = load_constructions(conn, town.id).await?;
        let editable = ruler.kingdom == town.kingdom;
        match command.kind.as_ref() {
            Some(command::Kind::InspectCity(_)) => {}
            Some(command::Kind::ApplyCityEdits(order)) => {
                if !editable {
                    return Ok(result(&command.request_id, false, "city_read_only"));
                }
                let edits = order
                    .edits
                    .iter()
                    .map(|edit| city::CellEdit {
                        x: edit.x as u16,
                        y: edit.y as u16,
                        layer: &edit.layer,
                        value: &edit.value,
                    })
                    .collect::<Vec<_>>();
                layout = match city::apply_edits(
                    &layout,
                    order.expected_revision,
                    &edits,
                    &self.city_catalog,
                ) {
                    Ok(layout) => layout,
                    Err(error) => return Ok(result(&command.request_id, false, error.0)),
                };
                save_layout(conn, &layout).await?;
            }
            Some(command::Kind::QueueBuilding(order)) => {
                if !editable {
                    return Ok(result(&command.request_id, false, "city_read_only"));
                }
                let Some(definition) = self.city_catalog.find(&order.building_id) else {
                    return Ok(result(&command.request_id, false, "unknown_building"));
                };
                let last_due = orders
                    .iter()
                    .filter(|order| order.status == "queued")
                    .map(|order| order.due_at_ms)
                    .max()
                    .unwrap_or(now_ms);
                let request = city::BuildingOrder {
                    order: ConstructionId(Uuid::new_v4()),
                    instance: orbis_core::city::BuildingInstanceId(Uuid::new_v4()),
                    kind: &order.building_id,
                    x: order.x as u16,
                    y: order.y as u16,
                    orientation: order.orientation as u8,
                    due_at_ms: last_due
                        .max(now_ms)
                        .saturating_add(definition.construction_ms),
                };
                let (next, construction) = match city::queue_building(
                    &layout,
                    order.expected_revision,
                    request,
                    orders
                        .iter()
                        .filter(|order| order.status == "queued")
                        .count(),
                    &self.city_catalog,
                    &self.city_balance,
                ) {
                    Ok(value) => value,
                    Err(error) => return Ok(result(&command.request_id, false, error.0)),
                };
                layout = next;
                save_layout(conn, &layout).await?;
                save_construction(conn, &construction).await?;
                schedule_action(
                    conn,
                    town.pos,
                    construction.due_at_ms,
                    &Action::CompleteConstruction {
                        city: town.id,
                        order: construction.id,
                        building: construction.building,
                    },
                    &format!("construction:{}", construction.id.0),
                )
                .await?;
                orders.push(construction);
            }
            Some(command::Kind::CancelConstruction(order)) => {
                if !editable {
                    return Ok(result(&command.request_id, false, "city_read_only"));
                }
                let Ok(order_id) = Uuid::parse_str(&order.order_id) else {
                    return Ok(result(
                        &command.request_id,
                        false,
                        "invalid_construction_id",
                    ));
                };
                let Some(construction) = orders
                    .iter_mut()
                    .find(|construction| construction.id.0 == order_id)
                else {
                    return Ok(result(&command.request_id, false, "construction_not_found"));
                };
                layout = match city::cancel_construction(
                    &layout,
                    order.expected_revision,
                    construction,
                    &self.city_catalog,
                ) {
                    Ok(layout) => layout,
                    Err(error) => return Ok(result(&command.request_id, false, error.0)),
                };
                construction.status = "canceled".to_owned();
                save_layout(conn, &layout).await?;
                sqlx::query(
                    "UPDATE construction_orders SET state=$2,canceled_at=now() WHERE id=$1 AND done_at IS NULL AND canceled_at IS NULL",
                )
                .bind(construction.id.0)
                .bind(Json(&*construction))
                .execute(&mut *conn)
                .await?;
                sqlx::query(
                    "UPDATE scheduled_events SET done_at=now() WHERE idempotency_key=$1 AND done_at IS NULL",
                )
                .bind(format!("construction:{}", construction.id.0))
                .execute(&mut *conn)
                .await?;
            }
            _ => unreachable!(),
        }
        Ok(city_snapshot(
            &town,
            &layout,
            &orders,
            &self.city_catalog,
            editable,
            now_ms,
        ))
    }

    async fn process_city_action(
        &self,
        conn: &mut PgConnection,
        action: &Action,
        now_ms: u64,
    ) -> Result<(), ServiceError> {
        match action {
            Action::CompleteConstruction {
                city,
                order,
                building,
            } => {
                let Some(mut layout) = load_layout(conn, *city, true).await? else {
                    return Ok(());
                };
                let Some(Json(mut construction)) = sqlx::query_scalar::<_, Json<Construction>>(
                    "SELECT state FROM construction_orders WHERE id=$1 FOR UPDATE",
                )
                .bind(order.0)
                .fetch_optional(&mut *conn)
                .await?
                else {
                    return Ok(());
                };
                if construction.status != "queued" {
                    return Ok(());
                }
                let (next, changed) =
                    city::complete_construction(&layout, *building, &self.city_catalog)?;
                if changed {
                    layout = next;
                    save_layout(conn, &layout).await?;
                    construction.status = "complete".to_owned();
                    sqlx::query(
                        "UPDATE construction_orders SET state=$2,done_at=now() WHERE id=$1 AND done_at IS NULL",
                    )
                    .bind(order.0)
                    .bind(Json(&construction))
                    .execute(&mut *conn)
                    .await?;
                }
            }
            Action::CityTick { city, tick } => {
                let Some(mut town) = load_city(conn, *city, true).await? else {
                    return Ok(());
                };
                let Some(layout) = load_layout(conn, *city, true).await? else {
                    return Ok(());
                };
                let (next, changed) = city::simulate_tick(
                    &layout,
                    *tick,
                    now_ms,
                    &self.city_catalog,
                    &self.city_balance,
                )?;
                if changed {
                    save_layout(conn, &next).await?;
                    town.population = next.population.total();
                    save_city(conn, &town).await?;
                    let next_tick = tick.saturating_add(1);
                    let due = now_ms.saturating_add(self.city_balance.simulation.tick_ms);
                    schedule_action(
                        conn,
                        town.pos,
                        due,
                        &Action::CityTick {
                            city: *city,
                            tick: next_tick,
                        },
                        &format!("city-tick:{}:{next_tick}", city.0),
                    )
                    .await?;
                }
            }
            Action::Arrive { .. } | Action::CompleteVillage { .. } => unreachable!(),
            Action::CompleteCaravan { .. } | Action::CompleteShip { .. } => unreachable!(),
        }
        Ok(())
    }
}

async fn load_city(
    conn: &mut PgConnection,
    id: CityId,
    for_update: bool,
) -> Result<Option<City>, sqlx::Error> {
    let query = if for_update {
        "SELECT state FROM cities WHERE id=$1 FOR UPDATE"
    } else {
        "SELECT state FROM cities WHERE id=$1"
    };
    Ok(sqlx::query_scalar::<_, Json<City>>(query)
        .bind(id.0)
        .fetch_optional(conn)
        .await?
        .map(|Json(city)| city))
}

async fn load_layout(
    conn: &mut PgConnection,
    city: CityId,
    for_update: bool,
) -> Result<Option<CityLayout>, sqlx::Error> {
    let query = if for_update {
        "SELECT state FROM city_layouts WHERE city_id=$1 FOR UPDATE"
    } else {
        "SELECT state FROM city_layouts WHERE city_id=$1"
    };
    Ok(sqlx::query_scalar::<_, Json<CityLayout>>(query)
        .bind(city.0)
        .fetch_optional(conn)
        .await?
        .map(|Json(layout)| layout))
}

async fn ensure_layout(
    conn: &mut PgConnection,
    town: &City,
    now_ms: u64,
    catalog: &CityCatalog,
    balance: &city::Balance,
) -> Result<CityLayout, ServiceError> {
    if let Some(mut layout) = load_layout(conn, town.id, true).await? {
        if layout.width != balance.grid.size_by_tier[0]
            || layout.height != balance.grid.size_by_tier[0]
        {
            city::resize_layout(&mut layout, balance.grid.size_by_tier[0])?;
            city::recompute_metrics(&mut layout, catalog)?;
            save_layout(conn, &layout).await?;
        }
        if layout.baseline_food_capacity == 0
            && layout
                .buildings
                .iter()
                .all(|building| building.kind == "house")
        {
            layout.baseline_food_capacity = balance.simulation.initial_food_capacity;
            layout.natural_water_coverage = balance.simulation.initial_natural_water_coverage;
            layout.events.retain(|event| event.kind != "famine");
            city::recompute_metrics(&mut layout, catalog)?;
            layout.revision += 1;
            save_layout(conn, &layout).await?;
        }
        return Ok(layout);
    }
    let layout = city::initial_layout(town, catalog, balance)?;
    save_layout(conn, &layout).await?;
    let tick = 1;
    schedule_action(
        conn,
        town.pos,
        now_ms.saturating_add(balance.simulation.tick_ms),
        &Action::CityTick {
            city: town.id,
            tick,
        },
        &format!("city-tick:{}:{tick}", town.id.0),
    )
    .await?;
    Ok(layout)
}

async fn save_layout(conn: &mut PgConnection, layout: &CityLayout) -> Result<(), sqlx::Error> {
    sqlx::query("INSERT INTO city_layouts(city_id,state,version,grid_version) VALUES($1,$2,$3,$4) ON CONFLICT(city_id) DO UPDATE SET state=EXCLUDED.state,version=EXCLUDED.version,grid_version=EXCLUDED.grid_version,updated_at=now()")
        .bind(layout.city.0)
        .bind(Json(layout))
        .bind(layout.revision as i64)
        .bind(if layout.width >= orbis_core::city::CITY_SIZE as u16 {2_i16} else {1_i16})
        .execute(conn)
        .await?;
    Ok(())
}

async fn load_constructions(
    conn: &mut PgConnection,
    city: CityId,
) -> Result<Vec<Construction>, sqlx::Error> {
    Ok(sqlx::query_scalar::<_, Json<Construction>>(
        "SELECT state FROM construction_orders WHERE city_id=$1 ORDER BY due_at,id",
    )
    .bind(city.0)
    .fetch_all(conn)
    .await?
    .into_iter()
    .map(|Json(order)| order)
    .collect())
}

async fn save_construction(
    conn: &mut PgConnection,
    construction: &Construction,
) -> Result<(), sqlx::Error> {
    sqlx::query("INSERT INTO construction_orders(id,city_id,state,due_at) VALUES($1,$2,$3,to_timestamp($4))")
        .bind(construction.id.0)
        .bind(construction.city.0)
        .bind(Json(construction))
        .bind(construction.due_at_ms as f64 / 1000.0)
        .execute(conn)
        .await?;
    Ok(())
}

async fn schedule_action(
    conn: &mut PgConnection,
    position: Position,
    due_at_ms: u64,
    action: &Action,
    idempotency_key: &str,
) -> Result<(), sqlx::Error> {
    let shard = world::shard(position);
    sqlx::query("INSERT INTO scheduled_events(id,shard,due_at,kind,payload,idempotency_key,worker_slot) VALUES($1,$2,to_timestamp($3),1,$4,$5,$6) ON CONFLICT(idempotency_key) DO NOTHING")
        .bind(Uuid::new_v4())
        .bind(shard as i64)
        .bind(due_at_ms as f64 / 1000.0)
        .bind(Json(action))
        .bind(idempotency_key)
        .bind(world::worker(shard) as i16)
        .execute(conn)
        .await?;
    Ok(())
}

fn city_snapshot(
    town: &City,
    layout: &CityLayout,
    orders: &[Construction],
    catalog: &CityCatalog,
    editable: bool,
    now_ms: u64,
) -> ServerMsg {
    let first_pending = orders
        .iter()
        .filter(|order| order.status == "queued")
        .map(|order| order.id)
        .next();
    ServerMsg {
        kind: Some(server_msg::Kind::City(CitySnapshot {
            city_id: town.id.0.to_string(),
            kingdom_id: town.kingdom.0.to_string(),
            name: town.name.clone(),
            width: u32::from(layout.width),
            height: u32::from(layout.height),
            revision: layout.revision,
            terrain_rle: city::encode_rle(&layout.terrain),
            roads_rle: city::encode_rle(&layout.roads),
            zones_rle: city::encode_rle(&layout.zones),
            buildings: layout
                .buildings
                .iter()
                .map(|building| BuildingState {
                    id: building.id.0.to_string(),
                    building_id: building.kind.clone(),
                    x: u32::from(building.x),
                    y: u32::from(building.y),
                    orientation: u32::from(building.orientation),
                    complete: building.complete,
                })
                .collect(),
            construction: orders
                .iter()
                .map(|order| ConstructionState {
                    id: order.id.0.to_string(),
                    building_instance_id: order.building.0.to_string(),
                    building_id: order.kind.clone(),
                    due_at_ms: order.due_at_ms as f64,
                    status: if order.status == "queued" && first_pending == Some(order.id) {
                        "active".to_owned()
                    } else {
                        order.status.clone()
                    },
                })
                .collect(),
            population: Some(PopulationState {
                farmers: layout.population.farmers,
                artisans: layout.population.artisans,
                soldiers: layout.population.soldiers,
                priests: layout.population.priests,
            }),
            metrics: Some(CityMetrics {
                water: layout.metrics.water,
                density: layout.metrics.density,
                defense: layout.metrics.defense,
                food_capacity: layout.metrics.food_capacity,
                housing_capacity: layout.metrics.housing_capacity,
                fire_risk: layout.metrics.fire_risk,
            }),
            events: layout
                .events
                .iter()
                .map(|event| CityEventState {
                    kind: event.kind.clone(),
                    started_at_ms: event.started_at_ms as f64,
                    ends_at_ms: event.ends_at_ms as f64,
                })
                .collect(),
            definitions: catalog
                .building
                .iter()
                .map(|definition| WireBuildingDefinition {
                    id: definition.id.clone(),
                    name_key: definition.name_key.clone(),
                    width: u32::from(definition.footprint[0]),
                    height: u32::from(definition.footprint[1]),
                    construction_ms: definition.construction_ms as f64,
                    zones: definition.zones.clone(),
                    water_required: definition.water_required,
                    visual_role: definition.visual_role.clone(),
                })
                .collect(),
            editable,
            now_ms: now_ms as f64,
        })),
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

// =========================================================================
// Economy persistence helpers (S1.3)
// =========================================================================

/// Hub decoded from relational columns; `pos` is stored as geography.
const HUB_COLUMNS: &str =
    "id,name,kingdom_id,stock,metal,fee::float8 AS fee,ST_Y(pos::geometry) AS lat,ST_X(pos::geometry) AS lon";

/// Book columns; `price` is stored as numeric.
const ORDER_COLUMNS: &str =
    "id,hub_id,kingdom_id,commodity,side,price::float8 AS price,quantity,filled_quantity,placed_at_ms,status";

fn hub_from_row(row: sqlx::postgres::PgRow) -> Hub {
    Hub {
        id: orbis_core::economy::HubId(row.get::<Uuid, _>("id")),
        name: row.get("name"),
        pos: Position {
            lat: row.get("lat"),
            lon: row.get("lon"),
        },
        kingdom: row.get::<Option<Uuid>, _>("kingdom_id").map(KingdomId),
        stock: row.get::<Json<Stock>, _>("stock").0,
        metal: row.get::<i64, _>("metal").max(0) as u64,
        fee: row.get("fee"),
    }
}

fn order_from_row(row: sqlx::postgres::PgRow) -> OrderState {
    OrderState {
        id: row.get::<Uuid, _>("id").to_string(),
        hub_id: row.get::<Uuid, _>("hub_id").to_string(),
        kingdom_id: row.get::<Uuid, _>("kingdom_id").to_string(),
        commodity: row.get("commodity"),
        side: row.get::<i16, _>("side") as i32,
        price: row.get("price"),
        quantity: row.get::<i32, _>("quantity").max(0) as u32,
        filled_quantity: row.get::<i32, _>("filled_quantity").max(0) as u32,
        placed_at_ms: row.get::<i64, _>("placed_at_ms").max(0) as u64,
        status: row.get("status"),
    }
}

// =========================================================================
// Economy command handlers (S1.3)
// =========================================================================

impl Context {
    /// Loads one hub into the domain struct; `lock` is an empty phrase, `FOR SHARE` or `FOR UPDATE`.
    async fn load_hub(
        &self,
        tx: &mut PgConnection,
        hub: Uuid,
        lock: &str,
    ) -> Result<Option<Hub>, ServiceError> {
        let sql = format!("SELECT {HUB_COLUMNS} FROM hubs WHERE id=$1 {lock}");
        let row = sqlx::query(&sql).bind(hub).fetch_optional(tx).await?;
        Ok(row.map(hub_from_row))
    }

    async fn economy_create_caravan(
        &self,
        tx: &mut PgConnection,
        ruler: &Ruler,
        order: &orbis_protocol::CreateCaravan,
        request_id: &str,
        now_ms: u64,
    ) -> Result<ServerMsg, ServiceError> {
        let city_id = Uuid::parse_str(&order.city_id)
            .map_err(|_| ServiceError::BadInput("invalid_city_id"))?;
        let hub_id =
            Uuid::parse_str(&order.hub_id).map_err(|_| ServiceError::BadInput("invalid_hub_id"))?;
        let caravan_id = CaravanId(Uuid::new_v4());

        // Verify city belongs to ruler's kingdom
        let city = load_city(tx, CityId(city_id), true)
            .await?
            .ok_or(ServiceError::BadInput("city_not_found"))?;
        if city.kingdom != ruler.kingdom {
            return Ok(result(request_id, false, "city_not_owned"));
        }

        // Verify hub exists
        let Some(hub) = self.load_hub(tx, hub_id, "FOR SHARE").await? else {
            return Ok(result(request_id, false, "hub_not_found"));
        };

        // Validate cargo weight against capacity
        let cargo: Stock = cargo_map(&order.cargo);
        let total_weight: f64 = cargo
            .iter()
            .filter_map(|(commodity, qty)| {
                self.economy_catalog
                    .find(commodity)
                    .map(|def| def.weight_kg * *qty as f64)
            })
            .sum();
        if total_weight > self.economy_balance.caravan.capacity_kg {
            return Ok(result(request_id, false, "cargo_over_capacity"));
        }

        // Calculate journey
        let clock = FixedClock(now_ms);
        let cities = load_cities(tx).await?;
        let plan = world::plan_caravan(
            ruler,
            world::CaravanOrder {
                id: caravan_id,
                from: CityId(city_id),
                to: HubId(hub_id),
                cargo: cargo.clone(),
                metal: order.metal,
            },
            &cities,
            &clock,
            &self.geo,
            &self.balance,
        )?;
        let (next, action, due) = plan;
        let ruler = next;

        // Persist caravan
        sqlx::query(
            "INSERT INTO caravans(id,kingdom_id,from_city,to_hub,journey,cargo,metal,revision,status) VALUES($1,$2,$3,$4,$5,$6,$7,1,'traveling')",
        )
        .bind(caravan_id.0)
        .bind(ruler.kingdom.0)
        .bind(city_id)
        .bind(hub_id)
        .bind(Json(serde_json::json!({
            "from": {"lat": city.pos.lat, "lon": city.pos.lon},
            "to": {"lat": hub.pos.lat, "lon": hub.pos.lon},
            "start_ms": now_ms,
            "due_ms": due,
        })))
        .bind(Json(&cargo))
        .bind(order.metal as i64)
        .execute(&mut *tx)
        .await?;

        // Schedule completion
        save_ruler(tx, &ruler).await?;
        let shard = world::shard(ruler.pos);
        sqlx::query(
            "INSERT INTO scheduled_events(id,shard,due_at,kind,payload,idempotency_key,worker_slot) VALUES($1,$2,to_timestamp($3),0,$4,$5,$6)",
        )
        .bind(Uuid::new_v4())
        .bind(shard as i64)
        .bind(due as f64 / 1000.0)
        .bind(Json(&action))
        .bind(format!("{}:{}", ruler.id.0, request_id))
        .bind(world::worker(shard) as i16)
        .execute(&mut *tx)
        .await?;

        Ok(result(request_id, true, ""))
    }

    async fn economy_create_ship(
        &self,
        tx: &mut PgConnection,
        ruler: &Ruler,
        order: &orbis_protocol::CreateShip,
        request_id: &str,
        now_ms: u64,
    ) -> Result<ServerMsg, ServiceError> {
        let city_id = Uuid::parse_str(&order.city_id)
            .map_err(|_| ServiceError::BadInput("invalid_city_id"))?;
        let hub_id =
            Uuid::parse_str(&order.hub_id).map_err(|_| ServiceError::BadInput("invalid_hub_id"))?;
        let ship_id = ShipId(Uuid::new_v4());

        // Verify city belongs to ruler's kingdom
        let city = load_city(tx, CityId(city_id), true)
            .await?
            .ok_or(ServiceError::BadInput("city_not_found"))?;
        if city.kingdom != ruler.kingdom {
            return Ok(result(request_id, false, "city_not_owned"));
        }

        // Verify hub exists
        let Some(hub) = self.load_hub(tx, hub_id, "FOR SHARE").await? else {
            return Ok(result(request_id, false, "hub_not_found"));
        };

        // Validate cargo weight against capacity
        let cargo: Stock = cargo_map(&order.cargo);
        let total_weight: f64 = cargo
            .iter()
            .filter_map(|(commodity, qty)| {
                self.economy_catalog
                    .find(commodity)
                    .map(|def| def.weight_kg * *qty as f64)
            })
            .sum();
        if total_weight > self.economy_balance.ship.capacity_kg {
            return Ok(result(request_id, false, "cargo_over_capacity"));
        }

        // Calculate journey
        let clock = FixedClock(now_ms);
        let cities = load_cities(tx).await?;
        let plan = world::plan_ship(
            ruler,
            world::ShipOrder {
                id: ship_id,
                from: CityId(city_id),
                to: HubId(hub_id),
                cargo: cargo.clone(),
                metal: order.metal,
            },
            &cities,
            &clock,
            &self.geo,
            &self.balance,
        )?;
        let (next, action, due) = plan;
        let ruler = next;

        // Persist ship
        sqlx::query(
            "INSERT INTO ships(id,kingdom_id,from_city,to_hub,journey,cargo,metal,revision,status) VALUES($1,$2,$3,$4,$5,$6,$7,1,'traveling')",
        )
        .bind(ship_id.0)
        .bind(ruler.kingdom.0)
        .bind(city_id)
        .bind(hub_id)
        .bind(Json(serde_json::json!({
            "from": {"lat": city.pos.lat, "lon": city.pos.lon},
            "to": {"lat": hub.pos.lat, "lon": hub.pos.lon},
            "start_ms": now_ms,
            "due_ms": due,
        })))
        .bind(Json(&cargo))
        .bind(order.metal as i64)
        .execute(&mut *tx)
        .await?;

        // Schedule completion
        save_ruler(tx, &ruler).await?;
        let shard = world::shard(ruler.pos);
        sqlx::query(
            "INSERT INTO scheduled_events(id,shard,due_at,kind,payload,idempotency_key,worker_slot) VALUES($1,$2,to_timestamp($3),0,$4,$5,$6)",
        )
        .bind(Uuid::new_v4())
        .bind(shard as i64)
        .bind(due as f64 / 1000.0)
        .bind(Json(&action))
        .bind(format!("{}:{}", ruler.id.0, request_id))
        .bind(world::worker(shard) as i16)
        .execute(&mut *tx)
        .await?;

        Ok(result(request_id, true, ""))
    }

    async fn economy_place_order(
        &self,
        tx: &mut PgConnection,
        ruler: &Ruler,
        order: &orbis_protocol::PlaceOrder,
        request_id: &str,
        now_ms: u64,
    ) -> Result<ServerMsg, ServiceError> {
        let hub_id =
            Uuid::parse_str(&order.hub_id).map_err(|_| ServiceError::BadInput("invalid_hub_id"))?;
        if !matches!(order.side, 0 | 1) {
            return Ok(result(request_id, false, "invalid_side"));
        }
        if order.quantity == 0 || !order.price.is_finite() || order.price <= 0.0 {
            return Ok(result(request_id, false, "invalid_order"));
        }

        // Verify hub and commodity exist. Resting orders record intent; the
        // physical exchange happens through the caravan arrival session.
        let hub_exists: bool = sqlx::query_scalar("SELECT EXISTS(SELECT 1 FROM hubs WHERE id=$1)")
            .bind(hub_id)
            .fetch_one(&mut *tx)
            .await?;
        if !hub_exists {
            return Ok(result(request_id, false, "hub_not_found"));
        }
        if self.economy_catalog.find(&order.commodity).is_none() {
            return Ok(result(request_id, false, "unknown_commodity"));
        }

        // Insert the resting order and return it.
        let created = sqlx::query(&format!(
            "INSERT INTO trade_orders(id,hub_id,kingdom_id,commodity,side,price,quantity,placed_at_ms) \
             VALUES($1,$2,$3,$4,$5,$6,$7,$8) RETURNING {ORDER_COLUMNS}",
        ))
        .bind(Uuid::new_v4())
        .bind(hub_id)
        .bind(ruler.kingdom.0)
        .bind(&order.commodity)
        .bind(order.side as i16)
        .bind(order.price)
        .bind(order.quantity as i32)
        .bind(now_ms as i64)
        .fetch_one(&mut *tx)
        .await?;
        Ok(ServerMsg {
            kind: Some(server_msg::Kind::Order(order_from_row(created))),
        })
    }

    async fn economy_cancel_order(
        &self,
        tx: &mut PgConnection,
        _ruler: &Ruler,
        order: &orbis_protocol::CancelOrder,
        request_id: &str,
        now_ms: u64,
    ) -> Result<ServerMsg, ServiceError> {
        let order_id = Uuid::parse_str(&order.order_id)
            .map_err(|_| ServiceError::BadInput("invalid_order_id"))?;
        let canceled =
            sqlx::query("UPDATE trade_orders SET status='canceled', canceled_at=to_timestamp($2) WHERE id=$1 AND status='open'")
            .bind(order_id).bind(now_ms as f64 / 1000.0).execute(&mut *tx).await?.rows_affected();
        if canceled == 0 {
            return Ok(result(request_id, false, "order_not_open"));
        }
        Ok(result(request_id, true, ""))
    }

    async fn economy_get_market(
        &self,
        tx: &mut PgConnection,
        _ruler: &Ruler,
        order: &orbis_protocol::GetMarket,
        request_id: &str,
        now_ms: u64,
    ) -> Result<ServerMsg, ServiceError> {
        let hub_id =
            Uuid::parse_str(&order.hub_id).map_err(|_| ServiceError::BadInput("invalid_hub_id"))?;
        let Some(hub) = self.load_hub(tx, hub_id, "").await? else {
            return Ok(result(request_id, false, "hub_not_found"));
        };

        // Get buy orders
        let buy_rows = sqlx::query(&format!(
            "SELECT {ORDER_COLUMNS} FROM trade_orders WHERE hub_id=$1 AND commodity=$2 AND side=0 AND status='open' ORDER BY price DESC, placed_at_ms",
        ))
        .bind(hub_id)
        .bind(&order.commodity)
        .fetch_all(&mut *tx)
        .await?;
        let buy_orders: Vec<OrderState> = buy_rows.into_iter().map(order_from_row).collect();

        // Get sell orders
        let sell_rows = sqlx::query(&format!(
            "SELECT {ORDER_COLUMNS} FROM trade_orders WHERE hub_id=$1 AND commodity=$2 AND side=1 AND status='open' ORDER BY price ASC, placed_at_ms",
        ))
        .bind(hub_id)
        .bind(&order.commodity)
        .fetch_all(&mut *tx)
        .await?;
        let sell_orders: Vec<OrderState> = sell_rows.into_iter().map(order_from_row).collect();

        // Get recent fills (last 20)
        let fill_rows = sqlx::query(&format!(
            "SELECT {ORDER_COLUMNS} FROM trade_orders WHERE hub_id=$1 AND commodity=$2 AND status='filled' ORDER BY placed_at_ms DESC LIMIT 20",
        ))
        .bind(hub_id)
        .bind(&order.commodity)
        .fetch_all(&mut *tx)
        .await?;
        let recent_fills: Vec<OrderState> = fill_rows.into_iter().map(order_from_row).collect();

        // Calculate local price
        let commodity_def = self.economy_catalog.find(&order.commodity);
        let local_price = if let Some(def) = commodity_def {
            let stock = hub.stock.get(&order.commodity).copied().unwrap_or(0);
            orbis_sim::economy::local_price(
                def.base_price,
                stock,
                def.demand_base,
                self.economy_balance.price.elasticity,
                self.economy_balance.price.min_multiplier,
                self.economy_balance.price.max_multiplier,
            )
        } else {
            0.0
        };

        let market = MarketSnapshot {
            hub_id: order.hub_id.clone(),
            commodity: order.commodity.clone(),
            buy_orders,
            sell_orders,
            recent_fills,
            local_price,
            observed_at_ms: now_ms,
        };

        Ok(ServerMsg {
            kind: Some(server_msg::Kind::Market(market)),
        })
    }

    async fn economy_get_hub(
        &self,
        tx: &mut PgConnection,
        _ruler: &Ruler,
        order: &orbis_protocol::GetHub,
        request_id: &str,
        _now_ms: u64,
    ) -> Result<ServerMsg, ServiceError> {
        let hub_id =
            Uuid::parse_str(&order.hub_id).map_err(|_| ServiceError::BadInput("invalid_hub_id"))?;

        let Some(hub) = self.load_hub(tx, hub_id, "").await? else {
            return Ok(result(request_id, false, "hub_not_found"));
        };

        Ok(ServerMsg {
            kind: Some(server_msg::Kind::Hub(hub_state(hub))),
        })
    }

    /// Full hub roster for the trade HUD (S1.3b); deterministic by name.
    async fn economy_list_hubs(
        &self,
        tx: &mut PgConnection,
        _ruler: &Ruler,
        _order: &orbis_protocol::ListHubs,
        _request_id: &str,
        _now_ms: u64,
    ) -> Result<ServerMsg, ServiceError> {
        let sql = format!("SELECT {HUB_COLUMNS} FROM hubs ORDER BY name");
        let rows = sqlx::query(&sql).fetch_all(tx).await?;
        let hubs: Vec<HubState> = rows
            .into_iter()
            .map(|row| hub_state(hub_from_row(row)))
            .collect();
        Ok(ServerMsg {
            kind: Some(server_msg::Kind::Hubs(HubsState { hubs })),
        })
    }
}

/// Domain `Hub` mapped into the protocol struct (shared by GetHub and ListHubs).
fn hub_state(hub: Hub) -> HubState {
    HubState {
        id: hub.id.0.to_string(),
        name: hub.name,
        pos: Some(LatLon {
            lat: hub.pos.lat,
            lon: hub.pos.lon,
        }),
        kingdom_id: hub.kingdom.map(|k| k.0.to_string()).unwrap_or_default(),
        stock: hub.stock.into_iter().collect(),
        metal: hub.metal,
        fee: hub.fee,
    }
}

// =========================================================================
// Economy event processing
// =========================================================================

fn cargo_map(cargo: &std::collections::HashMap<String, u32>) -> Stock {
    cargo
        .iter()
        .map(|(commodity, qty)| (commodity.clone(), *qty))
        .collect()
}

impl Context {
    async fn process_economy_action(
        &self,
        tx: &mut PgConnection,
        action: &Action,
        now_ms: u64,
    ) -> Result<(), ServiceError> {
        match action {
            Action::CompleteCaravan { caravan } => {
                self.process_caravan_arrival(tx, *caravan, now_ms).await?;
            }
            Action::CompleteShip { ship } => {
                self.process_ship_arrival(tx, *ship, now_ms).await?;
            }
            _ => {}
        }
        Ok(())
    }

    /// Trade session at a hub: sells the traveller's cargo commodity by
    /// commodity at the local price, charging the hub fee. Hub metal is finite,
    /// so an empty hub simply buys nothing. Commodity order follows the
    /// deterministic map iteration.
    async fn sell_cargo_to_hub(
        &self,
        tx: &mut PgConnection,
        hub: &mut Hub,
        cargo: &mut Stock,
        metal: &mut u64,
        now_ms: u64,
    ) -> Result<Stock, ServiceError> {
        let mut sold: Stock = Stock::new();
        for (commodity, quantity) in cargo.clone() {
            if quantity == 0 {
                continue;
            }
            let Some(def) = self.economy_catalog.find(&commodity) else {
                continue;
            };
            let stock = hub.stock.get(&commodity).copied().unwrap_or(0);
            let price = orbis_sim::economy::local_price(
                def.base_price,
                stock,
                def.demand_base,
                self.economy_balance.price.elasticity,
                self.economy_balance.price.min_multiplier,
                self.economy_balance.price.max_multiplier,
            );
            let traded = orbis_sim::economy::apply_trade(
                orbis_sim::economy::Party {
                    stock: &mut hub.stock,
                    metal: &mut hub.metal,
                },
                orbis_sim::economy::Party {
                    stock: cargo,
                    metal,
                },
                &commodity,
                orbis_core::economy::Side::Sell,
                quantity,
                price,
                hub.fee,
            )
            .is_ok();
            if !traded {
                continue;
            }
            sold.insert(commodity.clone(), quantity);
            sqlx::query(
                "INSERT INTO price_observations(id,hub_id,commodity,price,observed_at_ms,source) VALUES($1,$2,$3,$4,$5,$6)",
            )
            .bind(Uuid::new_v4())
            .bind(hub.id.0)
            .bind(&commodity)
            .bind(price)
            .bind(now_ms as i64)
            .bind("caravan")
            .execute(&mut *tx)
            .await?;
        }
        Ok(sold)
    }

    async fn process_caravan_arrival(
        &self,
        tx: &mut PgConnection,
        caravan_id: CaravanId,
        now_ms: u64,
    ) -> Result<(), ServiceError> {
        // Load caravan
        let caravan_row = sqlx::query(
            "SELECT id,kingdom_id,from_city,to_hub,cargo,metal,status FROM caravans WHERE id=$1 FOR UPDATE",
        )
        .bind(caravan_id.0)
        .fetch_optional(&mut *tx)
        .await?;
        let Some(row) = caravan_row else {
            return Ok(());
        };
        if row.get::<String, _>("status") != "traveling" {
            return Ok(());
        }

        let kingdom_id: Uuid = row.get("kingdom_id");
        let from_city_id: Uuid = row.get("from_city");
        let to_hub_id: Uuid = row.get("to_hub");
        let cargo: Json<Stock> = row.get("cargo");
        let metal: i64 = row.get("metal");

        let Some(mut hub) = self.load_hub(&mut *tx, to_hub_id, "FOR UPDATE").await? else {
            return Ok(());
        };

        // Trade session: sell the carried cargo at local prices.
        let mut travelling: Stock = cargo.0;
        let mut caravan_metal = metal.max(0) as u64;
        let sold = self
            .sell_cargo_to_hub(
                &mut *tx,
                &mut hub,
                &mut travelling,
                &mut caravan_metal,
                now_ms,
            )
            .await?;

        // Persist hub and caravan outcome
        sqlx::query("UPDATE hubs SET stock=$1,metal=$2 WHERE id=$3")
            .bind(Json(&hub.stock))
            .bind(hub.metal as i64)
            .bind(to_hub_id)
            .execute(&mut *tx)
            .await?;
        sqlx::query(
            "UPDATE caravans SET status='at_hub',cargo=$2,metal=$3,revision=revision+1 WHERE id=$1",
        )
        .bind(caravan_id.0)
        .bind(Json(&travelling))
        .bind(caravan_metal as i64)
        .execute(&mut *tx)
        .await?;

        // Log event
        sqlx::query(
            "INSERT INTO event_log(shard,kind,payload,account_id,kingdom_id,city_id) VALUES($1,2,$2,$3,$4,$5)",
        )
        .bind(world::shard(hub.pos) as i64)
        .bind(Json(serde_json::json!({
            "type": "caravan_arrived",
            "caravan_id": caravan_id.0,
            "hub_id": to_hub_id,
            "sold": sold,
            "cargo": travelling,
            "metal": caravan_metal,
        })))
        .bind(Option::<Uuid>::None)
        .bind(kingdom_id)
        .bind(from_city_id)
        .execute(&mut *tx)
        .await?;

        Ok(())
    }

    async fn process_ship_arrival(
        &self,
        tx: &mut PgConnection,
        ship_id: ShipId,
        now_ms: u64,
    ) -> Result<(), ServiceError> {
        // Load ship
        let ship_row = sqlx::query(
            "SELECT id,kingdom_id,from_city,to_hub,cargo,metal,status FROM ships WHERE id=$1 FOR UPDATE",
        )
        .bind(ship_id.0)
        .fetch_optional(&mut *tx)
        .await?;
        let Some(row) = ship_row else {
            return Ok(());
        };
        if row.get::<String, _>("status") != "traveling" {
            return Ok(());
        }

        let kingdom_id: Uuid = row.get("kingdom_id");
        let from_city_id: Uuid = row.get("from_city");
        let to_hub_id: Uuid = row.get("to_hub");
        let cargo: Json<Stock> = row.get("cargo");
        let metal: i64 = row.get("metal");

        let Some(mut hub) = self.load_hub(&mut *tx, to_hub_id, "FOR UPDATE").await? else {
            return Ok(());
        };

        // Trade session: sell the carried cargo at local prices.
        let mut travelling: Stock = cargo.0;
        let mut ship_metal = metal.max(0) as u64;
        let sold = self
            .sell_cargo_to_hub(&mut *tx, &mut hub, &mut travelling, &mut ship_metal, now_ms)
            .await?;

        // Persist hub and ship outcome
        sqlx::query("UPDATE hubs SET stock=$1,metal=$2 WHERE id=$3")
            .bind(Json(&hub.stock))
            .bind(hub.metal as i64)
            .bind(to_hub_id)
            .execute(&mut *tx)
            .await?;
        sqlx::query(
            "UPDATE ships SET status='at_hub',cargo=$2,metal=$3,revision=revision+1 WHERE id=$1",
        )
        .bind(ship_id.0)
        .bind(Json(&travelling))
        .bind(ship_metal as i64)
        .execute(&mut *tx)
        .await?;

        // Log event
        sqlx::query(
            "INSERT INTO event_log(shard,kind,payload,account_id,kingdom_id,city_id) VALUES($1,2,$2,$3,$4,$5)",
        )
        .bind(world::shard(hub.pos) as i64)
        .bind(Json(serde_json::json!({
            "type": "ship_arrived",
            "ship_id": ship_id.0,
            "hub_id": to_hub_id,
            "sold": sold,
            "cargo": travelling,
            "metal": ship_metal,
        })))
        .bind(Option::<Uuid>::None)
        .bind(kingdom_id)
        .bind(from_city_id)
        .execute(&mut *tx)
        .await?;

        Ok(())
    }
}
