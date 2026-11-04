use crate::world_service::{Context, ServiceError};
use orbis_core::world::Ruler;
use orbis_protocol::{EntityHandoff, HandoffAck};
use orbis_sim::world;
use prost::Message;
use sqlx::{types::Json, Row};
use uuid::Uuid;

impl Context {
    /// A lost reply keeps the durable transfer pending for a later retry.
    pub async fn flush_handoff(
        &self,
        nats: &async_nats::Client,
        slot: u16,
    ) -> Result<bool, ServiceError> {
        let row=sqlx::query("SELECT entity_id,revision,from_shard,to_shard,state FROM entity_handoffs WHERE source_worker=$1 AND acknowledged_at IS NULL ORDER BY revision LIMIT 1").bind(slot as i16).fetch_optional(&self.pool).await?;
        let Some(row) = row else { return Ok(false) };
        let id: Uuid = row.get("entity_id");
        let revision: i64 = row.get("revision");
        let from: i64 = row.get("from_shard");
        let to: i64 = row.get("to_shard");
        let handoff = EntityHandoff {
            entity_id: id.to_string(),
            revision: revision as u64,
            from_shard: from as u64,
            to_shard: to as u64,
            state: row.get("state"),
        };
        let subject = format!("orbis.shard.{}.handoff", world::worker(to as u64));
        let reply = tokio::time::timeout(
            std::time::Duration::from_millis(500),
            nats.request(subject, handoff.encode_to_vec().into()),
        )
        .await
        .map_err(|_| ServiceError::BadInput("handoff_timeout"))?
        .map_err(|_| ServiceError::BadInput("handoff_unavailable"))?;
        let ack = HandoffAck::decode(reply.payload)?;
        if !ack.accepted || ack.entity_id != handoff.entity_id || ack.revision != handoff.revision {
            return Err(ServiceError::BadInput("handoff_rejected"));
        }
        sqlx::query("UPDATE entity_handoffs SET acknowledged_at=now() WHERE entity_id=$1 AND revision=$2 AND acknowledged_at IS NULL").bind(id).bind(revision).execute(&self.pool).await?;
        Ok(true)
    }
    pub async fn receive_handoff(
        &self,
        transfer: &EntityHandoff,
        slot: u16,
    ) -> Result<HandoffAck, ServiceError> {
        let id = Uuid::parse_str(&transfer.entity_id)
            .map_err(|_| ServiceError::BadInput("invalid_handoff"))?;
        let incoming: Ruler = serde_json::from_slice(&transfer.state)?;
        let persisted = sqlx::query("SELECT shard,state FROM entities WHERE id=$1")
            .bind(id)
            .fetch_optional(&self.pool)
            .await?;
        let accepted = if let Some(row) = persisted {
            let current_shard: i64 = row.get("shard");
            let Json(current): Json<Ruler> = row.get("state");
            incoming.id.0 == id
                && incoming.revision == transfer.revision
                && world::worker(transfer.to_shard) == slot
                && current.revision >= transfer.revision
                && (current_shard as u64 == transfer.to_shard
                    || current.revision > transfer.revision)
        } else {
            false
        };
        Ok(HandoffAck {
            entity_id: transfer.entity_id.clone(),
            revision: transfer.revision,
            accepted,
        })
    }
}
