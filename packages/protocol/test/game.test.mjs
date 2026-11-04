import assert from 'node:assert/strict';
import test from 'node:test';
import { EntityHandoff, WorldSnapshot } from '../dist/orbis.js';
test('game snapshot and handoff retain full revisions and binary payload',()=>{
  const transfer=EntityHandoff.fromPartial({entityId:'ruler',revision:2n**64n-1n,fromShard:42n,toShard:43n,state:new Uint8Array([0,255,1])});
  assert.deepEqual(EntityHandoff.decode(EntityHandoff.encode(transfer).finish()),transfer);
  const snapshot=WorldSnapshot.fromPartial({ruler:{id:'ruler',revision:2n**64n-1n,pos:{lat:48.85,lon:2.35}},nowMs:1790000000000});
  assert.deepEqual(WorldSnapshot.decode(WorldSnapshot.encode(snapshot).finish()),snapshot);
});
