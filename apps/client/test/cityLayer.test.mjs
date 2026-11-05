import assert from 'node:assert/strict';
import test from 'node:test';
import { decodeRle } from '../dist/map/CityLayer.js';

test('city RLE decoder restores exactly 96 by 96 cells',()=>{
  const encoded=new Uint8Array([0,0,36]);
  const decoded=decodeRle(encoded,96*96);
  assert.equal(decoded.length,96*96);
  assert.ok(decoded.every(value=>value===0));
  assert.throws(()=>decodeRle(new Uint8Array([1,0,0]),1),/invalid_rle/);
});
