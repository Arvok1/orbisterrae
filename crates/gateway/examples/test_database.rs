//! Local integration-test lifecycle. Never accepts an ordinary database name.
use sqlx::{postgres::PgConnectOptions, Row};
use std::str::FromStr;
fn valid_name(name: &str) -> bool {
    name.strip_prefix("orbis_test_")
        .is_some_and(|suffix| suffix.len() == 32 && suffix.bytes().all(|b| b.is_ascii_hexdigit()))
}
#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let args: Vec<_> = std::env::args().collect();
    anyhow::ensure!(
        args.len() == 3 && valid_name(&args[2]),
        "test database requires orbis_test_ followed by 32 hexadecimal characters"
    );
    let name = &args[2];
    let url = std::env::var("DATABASE_URL")
        .unwrap_or_else(|_| "postgres://orbis:orbis@127.0.0.1:5432/orbis?sslmode=disable".into());
    let options = PgConnectOptions::from_str(&url)?;
    let database = match args[1].as_str() {
        "verify" | "verify-city" => name,
        _ => "postgres",
    };
    let pool = sqlx::postgres::PgPoolOptions::new()
        .max_connections(1)
        .connect_with(options.database(database))
        .await?;
    match args[1].as_str() {
        "create" => {
            sqlx::query(&format!("CREATE DATABASE {name}"))
                .execute(&pool)
                .await?;
        }
        "drop" => {
            sqlx::query(&format!("DROP DATABASE {name} WITH (FORCE)"))
                .execute(&pool)
                .await?;
        }
        "verify" => {
            let row=sqlx::query("SELECT (SELECT count(*) FROM cities WHERE state->>'active'='true') AS cities,(SELECT count(*) FROM entities) AS rulers,(SELECT count(*) FROM scheduled_events WHERE done_at IS NULL AND kind=0) AS pending,(SELECT count(*) FROM entity_handoffs) AS handoffs,(SELECT count(*) FROM entity_handoffs WHERE acknowledged_at IS NULL) AS unacknowledged,(SELECT count(*) FROM territory_cells_res8) AS cells").fetch_one(&pool).await?;
            let cities: i64 = row.get("cities");
            let rulers: i64 = row.get("rulers");
            let pending: i64 = row.get("pending");
            let handoffs: i64 = row.get("handoffs");
            let unacknowledged: i64 = row.get("unacknowledged");
            let cells: i64 = row.get("cells");
            println!("cities={cities} rulers={rulers} pending_events={pending} handoffs={handoffs} pending_handoffs={unacknowledged} territory_cells={cells}");
            anyhow::ensure!(
                cities == 100
                    && rulers == 101
                    && pending == 0
                    && handoffs >= 1
                    && unacknowledged == 0
                    && cells > 0,
                "S1.1 persisted world invariant failed"
            );
        }
        "verify-city" => {
            let row = sqlx::query(
                "SELECT (SELECT count(*) FROM cities WHERE state->>'active'='true') AS cities,
                        (SELECT count(*) FROM city_layouts) AS layouts,
                        (SELECT count(*) FROM construction_orders WHERE state->>'status'='queued' AND done_at IS NULL AND canceled_at IS NULL) AS pending_construction,
                        (SELECT count(*) FROM scheduled_events WHERE done_at IS NULL AND kind=1) AS pending_ticks,
                        (SELECT count(*) FROM scheduled_events WHERE done_at IS NULL AND kind=0) AS pending_world",
            )
            .fetch_one(&pool)
            .await?;
            let cities: i64 = row.get("cities");
            let layouts: i64 = row.get("layouts");
            let pending_construction: i64 = row.get("pending_construction");
            let pending_ticks: i64 = row.get("pending_ticks");
            let pending_world: i64 = row.get("pending_world");
            let defenses: Vec<f64> = sqlx::query(
                "SELECT (state->'metrics'->>'defense')::float8 AS defense FROM city_layouts ORDER BY defense",
            )
            .fetch_all(&pool)
            .await?
            .iter()
            .map(|row| row.get::<f64, _>("defense"))
            .collect();
            println!(
                "cities={cities} layouts={layouts} pending_construction={pending_construction} pending_ticks={pending_ticks} pending_world={pending_world} defenses={defenses:?}"
            );
            anyhow::ensure!(
                cities == 3
                    && layouts == 3
                    && pending_construction == 0
                    && pending_ticks == 3
                    && pending_world == 0
                    && defenses.len() == 3
                    && defenses.first().copied() == Some(0.0)
                    && defenses
                        .last()
                        .copied()
                        .is_some_and(|defense| defense >= 10.0),
                "S1.2 persisted city invariant failed"
            );
        }
        _ => anyhow::bail!("expected create, verify or drop"),
    }
    pool.close().await;
    Ok(())
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn only_owned_test_names_are_accepted() {
        assert!(!valid_name("orbis"));
        assert!(!valid_name("postgres"));
        assert!(!valid_name("orbis_test_x;DROP DATABASE orbis"));
        assert!(valid_name("orbis_test_0123456789abcdef0123456789abcdef"));
    }
}
