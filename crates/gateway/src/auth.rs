use crate::world_service::{Context, ServiceError};
use argon2::{
    password_hash::{phc::PasswordHash, PasswordHasher, PasswordVerifier},
    Argon2,
};
use jsonwebtoken::{Algorithm, DecodingKey, EncodingKey, Header, Validation};
use orbis_core::world::{AccountId, KingdomId, Position, Ruler, RulerId};
use serde::{Deserialize, Serialize};
use sqlx::Row;
use std::sync::Arc;
use tokio::sync::Semaphore;
use uuid::Uuid;

#[derive(Clone, Serialize, Deserialize)]
struct Claims {
    sub: String,
    exp: u64,
    iat: u64,
    iss: String,
}
pub struct Tokens {
    secret: Vec<u8>,
}
impl Tokens {
    pub fn new(secret: Vec<u8>) -> Result<Self, ServiceError> {
        if secret.len() < 32 {
            return Err(ServiceError::Configuration(
                "JWT secret must contain at least 32 bytes",
            ));
        }
        Ok(Self { secret })
    }
    pub fn load() -> Result<Self, ServiceError> {
        if let Ok(secret) = std::env::var("JWT_SECRET") {
            return Self::new(secret.into_bytes());
        }
        std::fs::create_dir_all(".local")?;
        let path = std::path::Path::new(".local/jwt-secret");
        if !path.exists() {
            use std::io::Write;
            let secret = format!("{}{}", Uuid::new_v4().simple(), Uuid::new_v4().simple());
            match std::fs::OpenOptions::new()
                .write(true)
                .create_new(true)
                .open(path)
            {
                Ok(mut file) => file.write_all(secret.as_bytes())?,
                Err(error) if error.kind() == std::io::ErrorKind::AlreadyExists => {}
                Err(error) => return Err(error.into()),
            }
        }
        Self::new(std::fs::read(path)?)
    }
    pub fn issue(&self, account: AccountId, now_secs: u64) -> Result<String, ServiceError> {
        let claims = Claims {
            sub: account.0.to_string(),
            exp: now_secs + 7 * 86400,
            iat: now_secs,
            iss: "orbis-local".into(),
        };
        Ok(jsonwebtoken::encode(
            &Header::new(Algorithm::HS256),
            &claims,
            &EncodingKey::from_secret(&self.secret),
        )?)
    }
    pub fn verify(&self, token: &str) -> Result<AccountId, ServiceError> {
        let mut validation = Validation::new(Algorithm::HS256);
        validation.set_issuer(&["orbis-local"]);
        validation.leeway = 0;
        let claims = jsonwebtoken::decode::<Claims>(
            token,
            &DecodingKey::from_secret(&self.secret),
            &validation,
        )
        .map_err(|_| ServiceError::Unauthorized)?
        .claims;
        Ok(AccountId(
            Uuid::parse_str(&claims.sub).map_err(|_| ServiceError::Unauthorized)?,
        ))
    }
}

#[derive(Deserialize)]
pub struct Credentials {
    pub username: String,
    pub password: String,
    pub start: Option<Position>,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct Session {
    pub token: String,
    pub username: String,
    pub account_id: Uuid,
    pub role: String,
}

pub struct Auth {
    pub tokens: Tokens,
    hashing: Arc<Semaphore>,
}
impl Auth {
    pub fn new(tokens: Tokens) -> Self {
        Self {
            tokens,
            hashing: Arc::new(Semaphore::new(4)),
        }
    }
    pub async fn authenticate(
        &self,
        context: &Context,
        credentials: Credentials,
        register: bool,
        now_ms: u64,
    ) -> Result<Session, ServiceError> {
        let username = credentials.username.trim().to_ascii_lowercase();
        if username.len() < 3
            || username.len() > 32
            || !username
                .bytes()
                .all(|b| b.is_ascii_alphanumeric() || b"_.-".contains(&b))
            || credentials.password.len() < 8
            || credentials.password.len() > 128
        {
            return Err(ServiceError::BadInput("invalid_credentials"));
        }
        let permit = self
            .hashing
            .clone()
            .acquire_owned()
            .await
            .map_err(|_| ServiceError::Unauthorized)?;
        let (account, role) = if register {
            let position = credentials.start.unwrap_or(context.balance.spawn);
            if !context.geo.is_land(position) {
                return Err(ServiceError::BadInput("land_required"));
            }
            let password = credentials.password;
            let hash = tokio::task::spawn_blocking(move || {
                let _permit = permit;
                Argon2::default()
                    .hash_password(password.as_bytes())
                    .map(|hash| hash.to_string())
                    .map_err(|_| ServiceError::Unauthorized)
            })
            .await
            .map_err(|_| ServiceError::Unauthorized)??;
            let account = AccountId(Uuid::new_v4());
            let kingdom = KingdomId(Uuid::new_v4());
            let ruler_id = RulerId(Uuid::new_v4());
            let ruler = Ruler {
                id: ruler_id,
                kingdom,
                pos: position,
                supplies: context.balance.ruler.initial_supplies,
                journey: None,
                founding_until: None,
                city: None,
                revision: 0,
            };
            let mut tx = context.pool.begin().await?;
            let inserted =
                sqlx::query("INSERT INTO accounts(id,username,password_hash) VALUES($1,$2,$3)")
                    .bind(account.0)
                    .bind(&username)
                    .bind(hash)
                    .execute(&mut *tx)
                    .await;
            if inserted
                .as_ref()
                .err()
                .and_then(sqlx::Error::as_database_error)
                .is_some_and(|error| error.is_unique_violation())
            {
                return Err(ServiceError::BadInput("username_taken"));
            }
            inserted?;
            sqlx::query(
                "INSERT INTO kingdoms(id,owner_account,name,culture) VALUES($1,$2,$3,'nomadic')",
            )
            .bind(kingdom.0)
            .bind(account.0)
            .bind(&username)
            .execute(&mut *tx)
            .await?;
            sqlx::query(
                "INSERT INTO entities(id,kind,kingdom_id,shard,state) VALUES($1,0,$2,$3,$4)",
            )
            .bind(ruler.id.0)
            .bind(kingdom.0)
            .bind(orbis_sim::world::shard(position) as i64)
            .bind(sqlx::types::Json(&ruler))
            .execute(&mut *tx)
            .await?;
            tx.commit().await?;
            (account, "player".to_owned())
        } else {
            let row = sqlx::query("SELECT id,password_hash,role FROM accounts WHERE username=$1")
                .bind(&username)
                .fetch_optional(&context.pool)
                .await?
                .ok_or(ServiceError::Unauthorized)?;
            let hash: String = row.try_get("password_hash")?;
            let password = credentials.password;
            let valid = tokio::task::spawn_blocking(move || {
                let _permit = permit;
                PasswordHash::new(&hash).is_ok_and(|hash| {
                    Argon2::default()
                        .verify_password(password.as_bytes(), &hash)
                        .is_ok()
                })
            })
            .await
            .map_err(|_| ServiceError::Unauthorized)?;
            if !valid {
                return Err(ServiceError::Unauthorized);
            }
            (AccountId(row.try_get("id")?), row.try_get("role")?)
        };
        Ok(Session {
            token: self.tokens.issue(account, now_ms / 1000)?,
            username,
            account_id: account.0,
            role,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn jwt_rejects_wrong_key_expired_and_malformed_tokens() {
        let tokens = Tokens::new(vec![1; 64]).unwrap();
        let other = Tokens::new(vec![2; 64]).unwrap();
        let account = AccountId(Uuid::from_u128(1));
        let now = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_secs();
        let token = tokens.issue(account, now).unwrap();
        assert_eq!(tokens.verify(&token).unwrap(), account);
        assert!(other.verify(&token).is_err());
        assert!(tokens.verify(&tokens.issue(account, 0).unwrap()).is_err());
        assert!(tokens.verify("bad").is_err());
        assert!(Tokens::new(vec![1; 8]).is_err());
    }
}
