import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import type { LatLon,ServerMsg,WorldSnapshot } from '@orbis/protocol';
import { BotClient } from './client.js';
import { percentile } from './scenario.js';
import { readVillageScenario } from './villageScenario.js';

const scenario=readVillageScenario(await readFile('scenarios/s1_1.toml','utf8'));
const fixture=JSON.parse(await readFile('scenarios/first_village_sites.json','utf8')) as {seed:number;sites:LatLon[];crossing:{from:LatLon;to:LatLon;sourceWorker:number;destinationWorker:number}};
assert.equal(fixture.seed,scenario.seed);assert.ok(fixture.sites.length>=scenario.bots);
const base=process.env.ORBIS_HTTP_URL??'http://127.0.0.1:8080';
const ws=process.env.ORBIS_WS_URL??'ws://127.0.0.1:8080/ws';
const prefix=`bot_${Date.now().toString(36)}`;
const clients:BotClient[]=[];const latencies:number[]=[];const started=performance.now();
const view={$case:'viewport' as const,viewport:{center:{lat:0,lon:0},zoom:0,widthPx:1024,heightPx:768}};
function accepted(response:ServerMsg) {
  assert.equal(response.kind?.$case,'result');
  if(response.kind?.$case!=='result')throw new Error('Expected result');
  assert.ok(response.kind.result.ok,response.kind.result.error);
}
async function snapshot(client:BotClient):Promise<WorldSnapshot> {
  const response=await client.request(view);assert.equal(response.kind?.$case,'world');
  if(response.kind?.$case!=='world')throw new Error('Expected world');return response.kind.world;
}
async function register(index:number,start:LatLon) {
  const username=`${prefix}_${index}`,password=`LocalTest_${crypto.randomUUID()}`;
  const response=await fetch(`${base}/api/register`,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({username,password,start})});
  assert.equal(response.status,200,await response.clone().text());
  const data=await response.json() as {token:string};
  const client=await BotClient.open(ws,scenario.timeoutMs);clients.push(client);
  accepted(await client.request({$case:'authenticate',authenticate:{token:data.token}}));
  return client;
}
try {
  const traveller=await register(1000,fixture.crossing.from);
  const original=await snapshot(traveller);
  accepted(await traveller.request({$case:'moveRuler',moveRuler:{destination:fixture.crossing.to}}));
  let arrived=await snapshot(traveller);const deadline=performance.now()+scenario.timeoutMs;
  while(arrived.ruler?.journey) {assert.ok(performance.now()<deadline,'Movement timed out');await new Promise(resolve=>setTimeout(resolve,200));arrived=await snapshot(traveller);}
  assert.equal(arrived.ruler?.id,original.ruler?.id);assert.deepEqual(arrived.ruler?.pos,fixture.crossing.to);
  assert.notEqual(fixture.crossing.sourceWorker,fixture.crossing.destinationWorker);
  let last:BotClient|undefined;
  for(let begin=0;begin<scenario.bots;begin+=scenario.concurrency) {
    await Promise.all(Array.from({length:Math.min(scenario.concurrency,scenario.bots-begin)},async(_,offset)=>{
      const index=begin+offset,client=await register(index,fixture.sites[index]);
      last=client;const world=await snapshot(client);
      const command={$case:'foundCity' as const,foundCity:{name:`Vila ${index}`,pos:world.ruler?.pos}};
      const start=performance.now();accepted(await client.request(command,'found-once'));latencies.push(performance.now()-start);
      if(index===0)accepted(await client.request(command,'found-once'));
    }));
    console.log(JSON.stringify({stage:'founding',bots:Math.min(scenario.bots,begin+scenario.concurrency)}));
  }
  assert.ok(last);let world=await snapshot(last);const until=performance.now()+scenario.timeoutMs;
  let confirmed:WorldSnapshot['cities']=[];
  while(confirmed.filter(city=>city.active).length<scenario.villages) {
    assert.ok(performance.now()<until,'Village completion timed out');confirmed=[];
    const founders=clients.filter(client=>client!==traveller);
    for(let begin=0;begin<founders.length;begin+=scenario.concurrency) {
      const snapshots=await Promise.all(founders.slice(begin,begin+scenario.concurrency).map(client=>snapshot(client)));
      assert.ok(snapshots.every(value=>value.cities.every(city=>city.kingdomId===value.ruler?.kingdomId)),'kingdom visibility remains authoritative');
      confirmed.push(...snapshots.flatMap(value=>value.cities));
    }
    if(confirmed.some(city=>!city.active))await new Promise(resolve=>setTimeout(resolve,250));
  }
  world=await snapshot(last);
  assert.equal(confirmed.length,scenario.villages);assert.ok(confirmed.every(city=>city.active));
  assert.equal(new Set(confirmed.map(city=>city.id)).size,scenario.villages);
  assert.ok(world.ruler);assert.equal(world.ruler.supplies,original.ruler!.supplies-world.villageCost);
  console.log(JSON.stringify({result:'passed',bots:scenario.bots,villages:confirmed.length,duplicateFounding:'idempotent',handoffRuler:arrived.ruler?.id===original.ruler?.id,
    p95FoundingMs:percentile(latencies,0.95),elapsedMs:performance.now()-started}));
} finally {for(const client of clients)client.close();}
