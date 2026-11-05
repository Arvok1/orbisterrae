use futures_util::StreamExt;
use orbis_core::world::AccountId;
use orbis_gateway::world_service::{result, Context, ServiceError};
use orbis_protocol::{
    command, envelope, server_msg, EntityHandoff, Envelope, HandoffAck, ServerMsg, ViewportSnapshot,
};
use prost::Message;
use std::time::{Duration, SystemTime, UNIX_EPOCH};
use uuid::Uuid;

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .expect("wall clock after epoch")
        .as_millis() as u64
}
#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt::init();
    let slot = std::env::args()
        .nth(2)
        .unwrap_or_else(|| "0".into())
        .parse::<u16>()?;
    anyhow::ensure!(slot < 2, "worker slot must be 0 or 1");
    let context = Context::connect().await?;
    let nats = async_nats::connect(
        std::env::var("NATS_URL").unwrap_or_else(|_| "nats://127.0.0.1:4222".into()),
    )
    .await?;
    let mut commands = nats
        .subscribe(format!("orbis.shard.{slot}.commands"))
        .await?;
    let mut handoffs = nats
        .subscribe(format!("orbis.shard.{slot}.handoff"))
        .await?;
    let mut diagnostic = if slot == 0 {
        Some(
            nats.queue_subscribe("orbis.demo.viewport", "demo-workers".into())
                .await?,
        )
    } else {
        None
    };
    nats.flush().await?;
    let events_context = context.clone();
    let events_nats = nats.clone();
    let scheduler = tokio::spawn(async move {
        let mut timer = tokio::time::interval(Duration::from_millis(100));
        loop {
            timer.tick().await;
            for _ in 0..64 {
                match events_context.process_due(slot, now_ms()).await {
                    Ok(true) => {}
                    Ok(false) => break,
                    Err(error) => {
                        tracing::error!(%error,slot,"Scheduled event failed; kept pending");
                        break;
                    }
                }
            }
            if let Err(error) = events_context.flush_handoff(&events_nats, slot).await {
                tracing::debug!(%error,slot,"Handoff will retry");
            }
        }
    });
    tracing::info!(slot, "Shard worker ready");
    loop {
        tokio::select! {
            message=commands.next()=>{
                let Some(message)=message else {break};let Some(reply)=message.reply else {continue};
                let Ok(envelope)=Envelope::decode(message.payload) else {continue};
                let Some(envelope::Body::Command(command))=envelope.body else {continue};
                let response=if command.kind.is_none() {result(&command.request_id,true,"")}
                    else {
                        let account=message.headers.as_ref().and_then(|headers|headers.get("x-orbis-account")).and_then(|id|Uuid::parse_str(id.as_str()).ok()).map(AccountId);
                        match account {
                            Some(account)=>{
                                let response=async {
                                    if orbis_sim::world::worker(context.actor_shard(account).await?)!=slot {return Ok(result(&command.request_id,false,"shard_moved"));}
                                    context.command(account,&command,now_ms()).await
                                }.await;
                                match response {Ok(response)=>response,Err(ServiceError::BadInput(code))=>result(&command.request_id,false,code),Err(ServiceError::Unauthorized)=>{tracing::warn!(slot,"actor lookup failed for authenticated account");result(&command.request_id,false,"authentication_required")},Err(error)=>{tracing::error!(%error,slot,"Command failed");result(&command.request_id,false,"server_error")}}
                            }
                            None=>result(&command.request_id,false,"authentication_required"),
                        }
                    };
                nats.publish(reply,Envelope{seq:envelope.seq,body:Some(envelope::Body::Server(response))}.encode_to_vec().into()).await?;
            }
            message=handoffs.next()=>{
                let Some(message)=message else {break};let Some(reply)=message.reply else {continue};
                let Ok(transfer)=EntityHandoff::decode(message.payload) else {continue};
                let ack=context.receive_handoff(&transfer,slot).await.unwrap_or(HandoffAck{entity_id:transfer.entity_id,revision:transfer.revision,accepted:false});
                nats.publish(reply,ack.encode_to_vec().into()).await?;
            }
            message=async {match diagnostic.as_mut() {Some(stream)=>stream.next().await,None=>std::future::pending().await}}=>{
                let Some(message)=message else {break};let Some(reply)=message.reply else {continue};
                let Ok(envelope)=Envelope::decode(message.payload) else {continue};let Some(envelope::Body::Command(command))=envelope.body else {continue};
                let response=match command.kind {
                    Some(command::Kind::Viewport(view))=>match orbis_sim::demo::snapshot(&view,10000) {Ok(entities)=>ServerMsg{kind:Some(server_msg::Kind::Snapshot(ViewportSnapshot{entities}))},Err(error)=>result(&command.request_id,false,&error.to_string())},
                    _=>result(&command.request_id,false,"command_not_available_in_diagnostic_world"),
                };
                nats.publish(reply,Envelope{seq:envelope.seq,body:Some(envelope::Body::Server(response))}.encode_to_vec().into()).await?;
            }
            _=tokio::signal::ctrl_c()=>break,
        }
    }
    scheduler.abort();
    Ok(())
}
