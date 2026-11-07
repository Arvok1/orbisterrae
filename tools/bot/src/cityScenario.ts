import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { CitySnapshot as CityWire, WorldSnapshot as WorldWire } from '@orbis/protocol';
import type { CityCellEdit, CitySnapshot, LatLon, ServerMsg, WorldSnapshot } from '@orbis/protocol';
import { BotClient } from './client.js';
import { readCityScenario } from './cityScenarioReader.js';

const scenarioPath = process.argv[2] ?? 'scenarios/s1_2.toml';
const scenario = readCityScenario(await readFile(scenarioPath, 'utf8'));
const fixture = JSON.parse(await readFile('scenarios/first_village_sites.json', 'utf8')) as {
  seed: number;
  sites: LatLon[];
};
assert.equal(fixture.seed, scenario.seed);
assert.ok(fixture.sites.length >= scenario.bots);

const base = process.env.ORBIS_HTTP_URL ?? 'http://127.0.0.1:8080';
const ws = process.env.ORBIS_WS_URL ?? 'ws://127.0.0.1:8080/ws';
const prefix = `citybot_${Date.now().toString(36)}`;
const clients: BotClient[] = [];
const tokens = new Map<BotClient,string>();
let offset = 0;
const started = performance.now();
const view = { $case: 'viewport' as const, viewport: { center: { lat: 0, lon: 0 }, zoom: 0, widthPx: 1024, heightPx: 768 } };

function accepted(response: ServerMsg) {
  assert.equal(response.kind?.$case, 'result');
  if (response.kind?.$case !== 'result') throw new Error('Expected result');
  assert.ok(response.kind.result.ok, `Command rejected: ${response.kind.result.error}`);
}
function describe(response: ServerMsg): string {
  return JSON.stringify(response.kind, (_key, value) => (typeof value === 'bigint' ? value.toString() : value)).slice(0, 500);
}
function asCity(response: ServerMsg): CitySnapshot {
  assert.equal(response.kind?.$case, 'city', describe(response));
  if (response.kind?.$case !== 'city') throw new Error('Expected city snapshot');
  return response.kind.city;
}
async function snapshot(client: BotClient): Promise<WorldSnapshot> {
  for (let attempt = 0; attempt < 6; attempt++) {
    const response = await client.request(view);
    if (response.kind?.$case === 'world') return response.kind.world;
    if (response.kind?.$case !== 'result' || !['worker_unavailable','server_error'].includes(response.kind.result.error)) {
      assert.equal(response.kind?.$case, 'world', describe(response));
    }
    await new Promise(resolve => setTimeout(resolve, 150));
  }
  throw new Error('world snapshot worker unavailable');
}
async function register(index: number, start: LatLon): Promise<BotClient> {
  const username = `${prefix}_${index}`;
  const password = `LocalTest_${crypto.randomUUID()}`;
  const response = await fetch(`${base}/api/register`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, password, start }),
  });
  assert.equal(response.status, 200, await response.clone().text());
  const data = await response.json() as { token: string };
  const client = await BotClient.open(ws, scenario.timeoutMs);
  clients.push(client);
  tokens.set(client,data.token);
  accepted(await client.request({ $case: 'authenticate', authenticate: { token: data.token } }));
  return client;
}
async function citySnapshot(client: BotClient, cityId: string): Promise<CitySnapshot> {
  for (let attempt = 0; attempt < 6; attempt++) {
    const response = await client.request({ $case: 'inspectCity', inspectCity: { cityId } });
    if (response.kind?.$case === 'city') return response.kind.city;
    if (response.kind?.$case !== 'result' || !['worker_unavailable','server_error'].includes(response.kind.result.error)) {
      assert.equal(response.kind?.$case, 'city', describe(response));
    }
    await new Promise(resolve => setTimeout(resolve, 150));
  }
  throw new Error('city snapshot worker unavailable');
}
function populationTotal(city: CitySnapshot): number {
  const population = city.population;
  return population ? population.farmers + population.artisans + population.soldiers + population.priests : 0;
}
function zoneCells(x0: number, x1: number, y0: number, y1: number, value: string): CityCellEdit[] {
  const edits: CityCellEdit[] = [];
  for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) edits.push({ x:x+offset, y:y+offset, layer: 'zone', value });
  return edits;
}
async function waitFor<T>(label: string, poll: () => Promise<T>, ready: (value: T) => boolean): Promise<T> {
  const deadline = performance.now() + scenario.timeoutMs;
  while (true) {
    let value: T;
    try {
      value = await poll();
    } catch (error) {
      if (String(error).includes('worker unavailable')) {
        assert.ok(performance.now() < deadline, `Timed out waiting for ${label}`);
        await new Promise(resolve => setTimeout(resolve, 150));
        continue;
      }
      throw error;
    }
    if (ready(value)) return value;
    assert.ok(performance.now() < deadline, `Timed out waiting for ${label}`);
    await new Promise(resolve => setTimeout(resolve, 150));
  }
}
async function ownCityId(client: BotClient): Promise<string> {
  return waitFor('own active city', async () => {
    const world = await snapshot(client);
    return world.cities.find(city => city.id === world.ruler?.cityId && city.active)?.id ?? '';
  }, id => id.length > 0);
}
async function foundOnce(client: BotClient, name: string): Promise<void> {
  for (let attempt = 0; attempt < 10; attempt++) {
    const world = await snapshot(client);
    const response = await client.request({
      $case: 'foundCity',
      foundCity: { name, pos: world.ruler?.pos },
    }, 'found-once');
    assert.equal(response.kind?.$case, 'result', describe(response));
    if (response.kind?.$case !== 'result') throw new Error('Expected founding result');
    if (response.kind.result.ok) return;
    if (response.kind.result.error === 'worker_unavailable') {
      await new Promise(resolve => setTimeout(resolve, 150));
      continue;
    }
    throw new Error(`Founding rejected: ${response.kind.result.error}`);
  }
  throw new Error('founding worker unavailable');
}
async function applyEdits(client: BotClient, cityId: string, edits: CityCellEdit[]): Promise<bigint> {
  let revision = (await citySnapshot(client, cityId)).revision;
  for (let attempt = 0; attempt < 10; attempt++) {
    const response = await client.request({
      $case: 'applyCityEdits',
      applyCityEdits: { cityId, expectedRevision: revision, edits },
    });
    if (response.kind?.$case === 'city') return response.kind.city.revision;
    if (response.kind?.$case === 'result'
      && (response.kind.result.error === 'stale_city_revision' || response.kind.result.error === 'worker_unavailable')) {
      revision = (await citySnapshot(client, cityId)).revision;
      continue;
    }
    throw new Error(`applyCityEdits failed: ${describe(response)}`);
  }
  throw new Error('applyCityEdits still stale after retries');
}
async function queue(client: BotClient, cityId: string, expected: bigint, buildingId: string, x: number, y: number): Promise<bigint> {
  x += offset;
  y += offset;
  let revision = expected;
  for (let attempt = 0; attempt < 10; attempt++) {
    const response = await client.request({
      $case: 'queueBuilding',
      queueBuilding: { cityId, expectedRevision: revision, buildingId, x, y, orientation: 0 },
    });
    if (response.kind?.$case === 'city') return response.kind.city.revision;
    if (response.kind?.$case === 'result'
      && (response.kind.result.error === 'stale_city_revision' || response.kind.result.error === 'worker_unavailable')) {
      const fresh = await citySnapshot(client, cityId);
      const alreadyQueued = fresh.construction.some(order => order.buildingId === buildingId && order.status === 'queued')
        || fresh.buildings.some(building => building.buildingId === buildingId && building.x === x && building.y === y);
      if (response.kind.result.error === 'worker_unavailable' && alreadyQueued) return fresh.revision;
      revision = fresh.revision;
      continue;
    }
    throw new Error(`queue ${buildingId} failed: ${describe(response)}`);
  }
  throw new Error(`queue ${buildingId} still stale after retries`);
}

try {
  const builder = await register(0, fixture.sites[0]);
  const idle = await register(1, fixture.sites[1]);
  const starving = await register(2, fixture.sites[2]);
  for (const [index, client] of [builder, idle, starving].entries()) {
    await foundOnce(client, `Vila ${index}`);
  }
  const builderCity = await ownCityId(builder);
  const idleCity = await ownCityId(idle);
  const starvingCity = await ownCityId(starving);
  const idleInitial = populationTotal(await citySnapshot(idle, idleCity));

  // Builder: tower for measurable defense, well and extra house for measurable growth.
  let city = await citySnapshot(builder, builderCity);
  assert.equal(city.width, 256);
  assert.equal(city.height, 256);
  offset = (city.width - 96) / 2;
  assert.equal(city.buildings.length, 3);
  assert.ok(city.editable);
  const initialDefense = city.metrics?.defense ?? 0;
  const edits = [
    ...zoneCells(44, 45, 38, 39, 'residential'),
    ...zoneCells(44, 45, 43, 44, 'residential'),
    ...zoneCells(20, 21, 20, 21, 'military'),
  ];
  let revision = await applyEdits(builder, builderCity, edits);
  revision = await queue(builder, builderCity, revision, 'tower', 20, 20);
  revision = await queue(builder, builderCity, revision, 'well', 44, 43);
  revision = await queue(builder, builderCity, revision, 'house', 44, 38);
  city = await waitFor('builder construction', () => citySnapshot(builder, builderCity),
    value => value.construction.length === 3 && value.construction.every(order => order.status === 'complete'));
  const defenseBuilt = city.metrics?.defense ?? 0;
  assert.ok(
    defenseBuilt - initialDefense >= scenario.minimumDefenseDelta,
    `defense delta ${defenseBuilt - initialDefense}`,
  );

  // The watered builder grows while the untouched idle village stays at 12.
  const grown = await waitFor('growth delta', async () => {
    const a = await citySnapshot(builder, builderCity);
    const b = await citySnapshot(idle, idleCity);
    return { a, b };
  }, ({ a, b }) => populationTotal(a) - populationTotal(b) >= scenario.minimumGrowthDelta);
  assert.equal(populationTotal(grown.b), idleInitial, 'a village without a well must not grow');
  assert.equal(grown.b.metrics?.defense ?? 0, 0, 'idle village has no defense');

  // Starving: four extra houses and no food; growth runs until famine fires.
  city = await citySnapshot(starving, starvingCity);
  revision = await applyEdits(starving, starvingCity, [
    ...zoneCells(28, 29, 30, 31, 'residential'),
    ...zoneCells(30, 38, 30, 31, 'residential'),
  ]);
  revision = await queue(starving, starvingCity, revision, 'well', 28, 30);
  for (const x of [30, 32, 34, 36]) revision = await queue(starving, starvingCity, revision, 'house', x, 30);
  await waitFor('starving construction', () => citySnapshot(starving, starvingCity),
    value => value.construction.length === 5 && value.construction.every(order => order.status === 'complete'));
  const famished = await waitFor('famine event', () => citySnapshot(starving, starvingCity),
    value => value.events.some(event => event.kind === 'famine'));
  const faminePopulation = populationTotal(famished);
  assert.ok(faminePopulation >= 32 && faminePopulation <= 33, `famine population ${faminePopulation}`);

  const world = await snapshot(builder);
  const ownWorlds = [world,await snapshot(idle),await snapshot(starving)];
  assert.ok(ownWorlds.every(value=>value.cities.length===1&&value.cities[0].kingdomId===value.ruler?.kingdomId),'player snapshots only expose their own kingdom');
  const confirmedCities=ownWorlds.flatMap(value=>value.cities);
  assert.equal(confirmedCities.filter(city=>city.active).length,scenario.cities);
  assert.equal(new Set(confirmedCities.map(city=>city.id)).size,scenario.cities);
  const foreign = await citySnapshot(builder,idleCity);
  assert.equal(foreign.editable,false,'foreign city is read-only');
  const unauthorized = await builder.request({$case:'applyCityEdits',applyCityEdits:{cityId:idleCity,expectedRevision:foreign.revision,edits:[{x:0,y:0,layer:'road',value:'road'}]}});
  assert.equal(unauthorized.kind?.$case,'result');
  if(unauthorized.kind?.$case==='result') assert.equal(unauthorized.kind.result.ok,false);
  const stale = await builder.request({$case:'applyCityEdits',applyCityEdits:{cityId:builderCity,expectedRevision:0n,edits:[{x:0,y:0,layer:'road',value:'road'}]}});
  assert.equal(stale.kind?.$case,'result');
  if(stale.kind?.$case==='result') assert.equal(stale.kind.result.ok,false);
  revision = await applyEdits(builder,builderCity,[...zoneCells(60,61,60,61,'residential'),{x:offset+10,y:offset+10,layer:'road',value:'road'}]);
  revision = await queue(builder,builderCity,revision,'house',60,60);
  const pending = await citySnapshot(builder,builderCity);
  const order = pending.construction.find(candidate=>candidate.buildingId==='house'&&(candidate.status==='active'||candidate.status==='queued'));
  assert.ok(order,'new construction can be cancelled');
  let cancelled=false;
  for(let attempt=0;attempt<10&&!cancelled;attempt++) {
    const fresh=await citySnapshot(builder,builderCity);
    const response=await builder.request({$case:'cancelConstruction',cancelConstruction:{cityId:builderCity,expectedRevision:fresh.revision,orderId:order.id}});
    if(response.kind?.$case==='city') cancelled=true;
    else if(response.kind?.$case!=='result'||response.kind.result.error!=='stale_city_revision') throw new Error(`cancel failed: ${describe(response)}`);
  }
  assert.ok(cancelled,'construction cancellation confirmed');
  builder.close();
  const reconnected=await BotClient.open(ws,scenario.timeoutMs);clients.push(reconnected);
  accepted(await reconnected.request({$case:'authenticate',authenticate:{token:tokens.get(builder)!}}));
  const restored=await citySnapshot(reconnected,builderCity);
  assert.ok(restored.revision>=revision,'reconnection restores confirmed revision');
  assert.equal(restored.buildings.filter(building=>building.id===pending.buildings.find(candidate=>candidate.x===offset+60&&candidate.y===offset+60)?.id).length,0,'cancelled building remains absent after reconnect');
  const exportRoot=process.env.ORBIS_ACCEPTANCE_EXPORT;
  if(exportRoot) {
    await mkdir(exportRoot,{recursive:true});
    await writeFile(join(exportRoot,'world.bin'),WorldWire.encode(await snapshot(reconnected)).finish());
    await writeFile(join(exportRoot,'world-idle.bin'),WorldWire.encode(await snapshot(idle)).finish());
    await writeFile(join(exportRoot,'world-starving.bin'),WorldWire.encode(await snapshot(starving)).finish());
    await writeFile(join(exportRoot,'city.bin'),CityWire.encode(restored).finish());
    await writeFile(join(exportRoot,'city-idle.bin'),CityWire.encode(await citySnapshot(reconnected,idleCity)).finish());
    await writeFile(join(exportRoot,'city-starving.bin'),CityWire.encode(await citySnapshot(reconnected,starvingCity)).finish());
  }
  console.log(JSON.stringify({
    result: 'passed',
    cities: confirmedCities.length,
    defenseDelta: defenseBuilt - initialDefense,
    growthDelta: populationTotal(grown.a) - populationTotal(grown.b),
    faminePopulation,
    reconnection: true, staleRevisionRejected: true, foreignCityReadOnly: true, cancellation: true,
    elapsedMs: performance.now() - started,
  }));
} finally {
  for (const client of clients) client.close();
}
