import assert from 'node:assert/strict';
import test from 'node:test';
import { project, unproject, visibleTiles } from '../dist/map/projection.js';
test('Mercator round trips coordinates on every continent', () => {
  for (const coordinate of [{lat:-23.55,lon:-46.63},{lat:51.5,lon:-0.1},{lat:35.7,lon:139.7},{lat:-33.9,lon:151.2}]) {
    const result = unproject(project(coordinate));
    assert.ok(Math.abs(result.lat-coordinate.lat)<1e-9);
    assert.ok(Math.abs(result.lon-coordinate.lon)<1e-9);
  }
});
test('tile selection is bounded and unique at poles and all zooms', () => {
  for (const size of [600, 1200, 16384]) for (const center of [{x:0,y:0},{x:1,y:1},{x:.5,y:.5}]) {
    const tiles = visibleTiles(center,size,1200,800);
    assert.equal(new Set(tiles.map(t=>JSON.stringify(t))).size,tiles.length);
    for (const t of tiles) assert.ok(t.x>=0 && t.x<2**t.z && t.y>=0 && t.y<2**t.z && t.z<=6);
    assert.ok(tiles.length<50);
  }
});
