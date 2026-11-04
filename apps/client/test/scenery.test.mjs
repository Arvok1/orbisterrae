import assert from 'node:assert/strict';
import test from 'node:test';
import { decoration,foliageBiome } from '../dist/map/scenery.js';
test('illustrative scenery is anchored, repeatable and remains within its cell',()=>{
  for (let x=0;x<50;x++) {
    const value=decoration(x,12,8,42);
    assert.deepEqual(value,decoration(x,12,8,42));
    assert.ok(value.point.x>x/256&&value.point.x<(x+1)/256);
    assert.ok(value.point.y>12/256&&value.point.y<13/256);
  }
  assert.notDeepEqual(decoration(3,12,8,42),decoration(3,12,8,43));
});
test('regional foliage uses the shipped biome palette and excludes sea and tundra',()=>{
  assert.equal(foliageBiome(0xa7,0xb9,0x6b,255),4);
  assert.equal(foliageBiome(0xe7,0xce,0x8d,255),13);
  assert.equal(foliageBiome(0xa7,0xb9,0x6b,0),0);
  assert.equal(foliageBiome(0xc9,0xcf,0xbb,255),0);
});
