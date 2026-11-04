import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

test('shipped terrain covers global sources and keeps valid geographic coordinates', () => {
  const data = JSON.parse(readFileSync(new URL('../src/assets/world-terrain-v1.json', import.meta.url), 'utf8'));
  assert.deepEqual(data.stats, {rivers:1473, lakes:412, biomes:847, relief:2249, symbols:2232});
  for (const [kind, count] of Object.entries(data.stats)) assert.equal(data[kind].length, count);
  for (const name of ['Amazonas', 'Nile', 'Mississippi', 'Yangtze']) {
    assert.ok(data.rivers.some(river => river.name === name), `Missing ${name}`);
  }
  const points = [
    ...data.rivers.flatMap(river => river.lines.flat()),
    ...data.lakes.flatMap(lake => lake.polygons.flat(2)),
    ...data.relief, ...data.symbols,
  ];
  for (const [lon, lat] of points) {
    assert.ok(Number.isFinite(lon) && lon >= -180 && lon <= 180);
    assert.ok(Number.isFinite(lat) && lat >= -90 && lat <= 90);
  }
  assert.equal(Object.keys(data.sources).length, 3);
  for (const source of Object.values(data.sources)) assert.match(source.sha256, /^[a-f0-9]{64}$/);
  const raster = readFileSync(new URL('../src/assets/world-biomes-v1.png', import.meta.url));
  assert.equal(raster.subarray(1,4).toString(), 'PNG');
  assert.equal(raster.readUInt32BE(16), 4096);
  assert.equal(raster.readUInt32BE(20), 4096);
});
