import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
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
    if (response.kind?.$case !== 'result' || response.kind.result.error !== 'worker_unavailable') {
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
  accepted(await client.request({ $case: 'authenticate', authenticate: { token: data.token } }));
  return client;
}
async function citySnapshot(client: BotClient, cityId: string): Promise<CitySnapshot> {
  for (let attempt = 0; attempt < 6; attempt++) {
    const response = await client.request({ $case: 'inspectCity', inspectCity: { cityId } });
    if (response.kind?.$case === 'city') return response.kind.city;
    if (response.kind?.$case !== 'result' || response.kind.result.error !== 'worker_unavailable') {
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
  for (let y = y0; y <= y1; y++) for (let x = x0; x <= x1; x++) edits.push({ x, y, layer: 'zone', value });
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
  assert.equal(city.width, 96);
  assert.equal(city.height, 96);
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
  assert.equal(world.cities.filter(city => city.active).length, scenario.cities);
  assert.equal(new Set(world.cities.map(city => city.id)).size, scenario.cities);
  console.log(JSON.stringify({
    result: 'passed',
    cities: world.cities.length,
    defenseDelta: defenseBuilt - initialDefense,
    growthDelta: populationTotal(grown.a) - populationTotal(grown.b),
    faminePopulation,
    elapsedMs: performance.now() - started,
  }));
} finally {
  for (const client of clients) client.close();
}
