use anyhow::{bail, Context};
use serde_json::json;
use sqlx::{postgres::PgPoolOptions, Row};

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    let mut args = std::env::args().skip(1);
    let noun = args.next().unwrap_or_default();
    let action = args.next().unwrap_or_default();
    if noun != "role" || !matches!(action.as_str(), "grant" | "revoke") {
        bail!("usage: orbis-admin role <grant|revoke> --username <name> --reason <text>");
    }

    let mut username = None;
    let mut reason = None;
    while let Some(argument) = args.next() {
        match argument.as_str() {
            "--username" => username = args.next(),
            "--reason" => reason = args.next(),
            _ => bail!("unknown argument: {argument}"),
        }
    }
    let username = username
        .filter(|value| !value.trim().is_empty())
        .context("--username is required")?
        .trim()
        .to_ascii_lowercase();
    let reason = reason
        .filter(|value| !value.trim().is_empty())
        .context("--reason is required")?;
    let database_url = std::env::var("DATABASE_URL")
        .unwrap_or_else(|_| "postgres://orbis:orbis@127.0.0.1:5432/orbis".into());
    let pool = PgPoolOptions::new()
        .max_connections(2)
        .connect(&database_url)
        .await
        .context("connect to Orbis database")?;
    let mut tx = pool.begin().await?;
    let row = sqlx::query("SELECT id,role FROM accounts WHERE username=$1 FOR UPDATE")
        .bind(&username)
        .fetch_optional(&mut *tx)
        .await?
        .with_context(|| format!("account '{username}' was not found"))?;
    let account_id: uuid::Uuid = row.try_get("id")?;
    let previous_role: String = row.try_get("role")?;
    let next_role = if action == "grant" { "admin" } else { "player" };
    sqlx::query("UPDATE accounts SET role=$1 WHERE id=$2")
        .bind(next_role)
        .bind(account_id)
        .execute(&mut *tx)
        .await?;
    sqlx::query("INSERT INTO admin_audit(account_id,action,filters) VALUES($1,$2,$3)")
        .bind(account_id)
        .bind(format!("role_{action}"))
        .bind(sqlx::types::Json(json!({
            "actor": "local-cli",
            "username": username,
            "reason": reason,
            "previousRole": previous_role,
            "nextRole": next_role,
        })))
        .execute(&mut *tx)
        .await?;
    tx.commit().await?;
    println!("{username}: {previous_role} -> {next_role}");
    Ok(())
}
