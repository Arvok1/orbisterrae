import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import type { CityCellEdit, CitySnapshot, LatLon, ServerMsg, WorldSnapshot } from '@orbis/protocol';
import { BotClient } from './client.js';

const base = process.env.ORBIS_HTTP_URL ?? 'http://127.0.0.1:8080';
const ws = process.env.ORBIS_WS_URL ?? 'ws://127.0.0.1:8080/ws';
const credentialsPath = '.local/demo-world-credentials.json';
const sites = (JSON.parse(await readFile('scenarios/first_village_sites.json', 'utf8')) as { sites: LatLon[] }).sites.slice(0, 12);
const names = ['Aurora', 'Valedouro', 'Pedra Alta', 'Bosque Antigo', 'Porto Cinzento', 'Sol Nascente', 'Serra Negra', 'Águas Claras'];
const showcase = [
  { building: 'well', zone: 'residential' },
  { building: 'tower', zone: 'military' },
  { building: 'field', zone: 'agricultural' },
  { building: 'wood_workshop', zone: 'industrial' },
  { building: 'forge', zone: 'industrial' },
  { building: 'market', zone: 'civic' },
  { building: 'temple', zone: 'civic' },
  { building: 'barracks', zone: 'military' },
] as const;

interface StoredCredentials { password: string }
interface Session { token: string }

function accepted(response: ServerMsg): void {
  assert.equal(response.kind?.$case, 'result');
  if (response.kind?.$case !== 'result' || !response.kind.result.ok) {
    throw new Error(`Command rejected: ${response.kind?.$case === 'result' ? response.kind.result.error : 'unexpected response'}`);
  }
}

async function credentials(): Promise<StoredCredentials> {
  try {
    return JSON.parse(await readFile(credentialsPath, 'utf8')) as StoredCredentials;
  } catch {
    const stored = { password: `Demo_${crypto.randomUUID()}` };
    await mkdir('.local', { recursive: true });
    await writeFile(credentialsPath, JSON.stringify(stored, null, 2), { encoding: 'utf8', mode: 0o600 });
    return stored;
  }
}

async function loginOrRegister(index: number, password: string): Promise<BotClient> {
  const username = `demo_v1_${index.toString().padStart(2, '0')}`;
  let response = await fetch(`${base}/api/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ username, password }),
  });
  if (response.status === 401) {
    response = await fetch(`${base}/api/register`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ username, password, start: sites[index] }),
    });
  }
  assert.equal(response.status, 200, await response.clone().text());
  const session = await response.json() as Session;
  const client = await BotClient.open(ws, 15_000);
  accepted(await client.request({ $case: 'authenticate', authenticate: { token: session.token } }));
  return client;
}

async function snapshot(client: BotClient): Promise<WorldSnapshot> {
  const response = await client.request({ $case: 'viewport', viewport: { center: { lat: 0, lon: 0 }, zoom: 0, widthPx: 1280, heightPx: 720 } });
  if (response.kind?.$case !== 'world') throw new Error('World snapshot unavailable');
  return response.kind.world;
}

async function citySnapshot(client: BotClient, cityId: string): Promise<CitySnapshot> {
  const response = await client.request({ $case: 'inspectCity', inspectCity: { cityId } });
  if (response.kind?.$case !== 'city') throw new Error('City snapshot unavailable');
  return response.kind.city;
}

async function waitForCity(client: BotClient): Promise<string> {
  const deadline = Date.now() + 25_000;
  while (Date.now() < deadline) {
    const world = await snapshot(client);
    const own = world.cities.find(city => city.id === world.ruler?.cityId && city.active);
    if (own) return own.id;
    await new Promise(resolve => setTimeout(resolve, 500));
  }
  throw new Error('Timed out waiting for demo city');
}

function zone(x: number, y: number, value: string): CityCellEdit[] {
  const edits: CityCellEdit[] = [];
  for (let row = y; row < y + 6; row++) for (let column = x; column < x + 6; column++) {
    edits.push({ x: column, y: row, layer: 'zone', value });
  }
  return edits;
}

async function decorate(client: BotClient, cityId: string, index: number): Promise<void> {
  const definition = showcase[index];
  let city = await citySnapshot(client, cityId);
  if (city.buildings.some(building => building.buildingId === definition.building)
    || city.construction.some(order => order.buildingId === definition.building)) return;
  const x = 18 + index * 8;
  const y = 22;
  let response = await client.request({
    $case: 'applyCityEdits',
    applyCityEdits: { cityId, expectedRevision: city.revision, edits: zone(x, y, definition.zone) },
  });
  if (response.kind?.$case !== 'city') throw new Error(`Could not zone demo city ${index}`);
  city = response.kind.city;
  response = await client.request({
    $case: 'queueBuilding',
    queueBuilding: { cityId, expectedRevision: city.revision, buildingId: definition.building, x, y, orientation: 0 },
  });
  if (response.kind?.$case !== 'city') throw new Error(`Could not queue ${definition.building}`);
}

const stored = await credentials();
const clients: BotClient[] = [];
try {
  for (let index = 0; index < sites.length; index++) clients.push(await loginOrRegister(index, stored.password));
  for (let index = 0; index < 8; index++) {
    const client = clients[index];
    const world = await snapshot(client);
    if (!world.ruler?.cityId) {
      const response = await client.request({ $case: 'foundCity', foundCity: { name: names[index], pos: world.ruler?.pos } }, `demo-found-${index}`);
      accepted(response);
    }
    const cityId = await waitForCity(client);
    await decorate(client, cityId, index);
  }
  for (let index = 8; index < 12; index++) {
    const client = clients[index];
    const world = await snapshot(client);
    if (!world.ruler?.journey && world.ruler?.pos) {
      const destination = { lat: world.ruler.pos.lat + 0.015, lon: world.ruler.pos.lon + 0.015 };
      const response = await client.request({ $case: 'moveRuler', moveRuler: { destination } });
      if (response.kind?.$case === 'result' && !response.kind.result.ok) {
        console.warn(`Movement ${index} skipped: ${response.kind.result.error}`);
      }
    }
  }
  console.log(JSON.stringify({ result: 'ready', accounts: 12, cities: 8, credentials: credentialsPath }));
} finally {
  for (const client of clients) client.close();
}
