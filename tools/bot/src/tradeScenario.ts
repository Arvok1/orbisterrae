import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import type { LatLon, ServerMsg, WorldSnapshot } from '@orbis/protocol';
import { BotClient } from './client.js';
import { readTradeScenario } from './tradeScenarioReader.js';

const scenarioPath = process.argv[2] ?? 'scenarios/s1_3.toml';
const scenario = readTradeScenario(await readFile(scenarioPath, 'utf8'));
const fixture = JSON.parse(await readFile('scenarios/first_village_sites.json', 'utf8')) as {
  seed: number;
  sites: LatLon[];
};
assert.equal(fixture.seed, scenario.seed);
assert.ok(fixture.sites.length >= 1);

const base = process.env.ORBIS_HTTP_URL ?? 'http://127.0.0.1:8080';
const ws = process.env.ORBIS_WS_URL ?? 'ws://127.0.0.1:8080/ws';
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
function totalStock(stock: Record<string, number> | undefined): number {
  return Object.values(stock ?? {}).reduce((sum, quantity) => sum + quantity, 0);
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
async function register(start: LatLon): Promise<BotClient> {
  const username = `tradebot_${Date.now().toString(36)}`;
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

try {
  const trader = await register(fixture.sites[0]);
  const rosterSource = await waitFor('hub roster', async () => {
    const response = await trader.request({ $case: 'listHubs', listHubs: {} });
    if (response.kind?.$case !== 'hubs') return null;
    return response.kind.hubs.hubs;
  }, roster => roster !== null);
  if (rosterSource === null) throw new Error('no hub roster');
  const roster = rosterSource;
  assert.ok(roster.length >= scenario.minimumHubs, `roster has ${roster.length} hubs`);

  // Roster is deterministic: named order, every hub alive with fee, metal and a map position.
  const names = roster.map(hub => hub.name);
  assert.deepEqual(names, [...names].sort());
  for (const hub of roster) {
    assert.ok(hub.fee > 0, `${hub.name} must charge a fee`);
    assert.ok(hub.metal >= BigInt(scenario.initialHubMetal), `${hub.name} starts liquid`);
    assert.ok(hub.pos, `${hub.name} must map to a position`);
    assert.ok(totalStock(hub.stock) > 0, `${hub.name} must stock goods`);
  }
  const hub = roster[0];
  assert.ok(scenario.quantity * 1 <= 2_000 - 0, 'commodity weight stays inside caravan capacity');

  // Market read: the local price exists before any trade happens.
  const marketSource = await waitFor('market snapshot', async () => {
    const response = await trader.request({ $case: 'getMarket', getMarket: { hubId: hub.id, commodity: scenario.commodity } });
    if (response.kind?.$case !== 'market') return null;
    return response.kind.market;
  }, market => market !== null);
  if (marketSource === null) throw new Error('no market snapshot');
  assert.ok(marketSource.localPrice > 0, 'local price is positive');

  // A caravan needs an own, active city.
  await foundOnce(trader, 'Vila do caravaneiro');
  const cityId = await ownCityId(trader);

  const before = roster[0].stock[scenario.commodity] ?? 0;
  const metalBefore = hub.metal;
  const world = await snapshot(trader);
  const rulerRevision = world.ruler?.revision ?? 0n;
  accepted(await trader.request({
    $case: 'createCaravan',
    createCaravan: { cityId, hubId: hub.id, expectedRevision: rulerRevision, cargo: { [scenario.commodity]: scenario.quantity }, metal: 0n },
  }, 'dispatch-caravan'));

  // Over capacity: 3000 units of a 1 kg commodity cannot fit one caravan.
  const overloaded = await trader.request({
    $case: 'createCaravan',
    createCaravan: { cityId, hubId: hub.id, expectedRevision: rulerRevision, cargo: { [scenario.commodity]: 3_000 }, metal: 0n },
  }, 'overload-caravan');
  assert.equal(overloaded.kind?.$case, 'result', describe(overloaded));
  if (overloaded.kind?.$case !== 'result') throw new Error('Expected result');
  assert.equal(overloaded.kind.result.ok, false, 'over-capacity cargo is rejected');
  assert.equal(overloaded.kind.result.error, 'cargo_over_capacity');

  // The caravan session closes on arrival: hub stock gains the cargo, hub metal pays for it.
  const arrivedSource = await waitFor('caravan arrival', async () => {
    const response = await trader.request({ $case: 'getHub', getHub: { hubId: hub.id } });
    if (response.kind?.$case !== 'hub') return null;
    return response.kind.hub;
  }, after => after !== null && (after.stock[scenario.commodity] ?? 0) >= before + scenario.quantity);
  if (arrivedSource === null) throw new Error('caravan never arrived');
  const arrived = arrivedSource;
  assert.ok(arrived.metal < metalBefore, `hub metal ${arrived.metal} must drop from ${metalBefore}`);

  console.log(JSON.stringify({
    result: 'passed',
    hubs: roster.length,
    commodity: scenario.commodity,
    dispatched: scenario.quantity,
    hubMetalDelta: (metalBefore - arrived.metal).toString(),
    overloadRejected: true,
    elapsedMs: performance.now() - started,
  }));
} finally {
  for (const client of clients) client.close();
}
