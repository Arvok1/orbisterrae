import assert from 'node:assert/strict';
import test from 'node:test';
import { decodeRle } from '../dist/map/CityLayer.js';

test('city RLE decoder restores exactly 256 by 256 cells',()=>{
  const encoded=new Uint8Array([0,255,255,0,1,0]);
  const decoded=decodeRle(encoded,256*256);
  assert.equal(decoded.length,256*256);
  assert.ok(decoded.every(value=>value===0));
  assert.throws(()=>decodeRle(new Uint8Array([1,0,0]),1),/invalid_rle/);
});
