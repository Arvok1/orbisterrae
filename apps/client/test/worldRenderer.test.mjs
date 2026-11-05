import assert from 'node:assert/strict';
import test from 'node:test';
import { lodForZoom } from '../dist/map/lod.js';

test('LOD boundaries keep city data out of regional view',()=>{
  assert.equal(lodForZoom(0),'global');
  assert.equal(lodForZoom(8),'regional');
  assert.equal(lodForZoom(14.99),'regional');
  assert.equal(lodForZoom(15),'city');
});
