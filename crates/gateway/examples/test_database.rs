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
    let pool = sqlx::postgres::PgPoolOptions::new()
        .max_connections(1)
        .connect_with(options.database(if args[1] == "verify" {
            name
        } else {
            "postgres"
        }))
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
            let row=sqlx::query("SELECT (SELECT count(*) FROM cities WHERE state->>'active'='true') AS cities,(SELECT count(*) FROM entities) AS rulers,(SELECT count(*) FROM scheduled_events WHERE done_at IS NULL) AS pending,(SELECT count(*) FROM entity_handoffs) AS handoffs,(SELECT count(*) FROM entity_handoffs WHERE acknowledged_at IS NULL) AS unacknowledged,(SELECT count(*) FROM territory_cells) AS cells").fetch_one(&pool).await?;
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
