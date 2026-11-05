import assert from 'node:assert/strict';
import test from 'node:test';
import { AdminSnapshot, AdminSnapshotRequest, CitySnapshot, EntityHandoff, WorldSnapshot } from '../dist/orbis.js';
test('game snapshot and handoff retain full revisions and binary payload',()=>{
  const transfer=EntityHandoff.fromPartial({entityId:'ruler',revision:2n**64n-1n,fromShard:42n,toShard:43n,state:new Uint8Array([0,255,1])});
  assert.deepEqual(EntityHandoff.decode(EntityHandoff.encode(transfer).finish()),transfer);
  const snapshot=WorldSnapshot.fromPartial({ruler:{id:'ruler',revision:2n**64n-1n,pos:{lat:48.85,lon:2.35}},nowMs:1790000000000});
  assert.deepEqual(WorldSnapshot.decode(WorldSnapshot.encode(snapshot).finish()),snapshot);
});

test('city snapshot retains revision, RLE layers and authoritative state',()=>{
  const snapshot=CitySnapshot.fromPartial({
    cityId:'city',revision:2n**64n-1n,width:96,height:96,
    roadsRle:new Uint8Array([0,255,0,1,1,0]),editable:true,
    buildings:[{id:'house-1',buildingId:'house',x:47,y:48,complete:true}],
    population:{farmers:12},metrics:{water:0.75,density:0.25,defense:4},
    definitions:[{id:'house',nameKey:'building.house',width:2,height:2,zones:['residential']}]
  });
  assert.deepEqual(CitySnapshot.decode(CitySnapshot.encode(snapshot).finish()),snapshot);
});

test('admin filters and live events retain 64-bit cursors',()=>{
  const request=AdminSnapshotRequest.fromPartial({afterEventSeq:2n**64n-1n,accountId:'account',cityId:'city',shard:8n,eventKind:3,filterShard:true,filterEventKind:true,includeWorld:true});
  assert.deepEqual(AdminSnapshotRequest.decode(AdminSnapshotRequest.encode(request).finish()),request);
  const snapshot=AdminSnapshot.fromPartial({nextEventSeq:2n**64n-1n,events:[{seq:2n**64n-1n,shard:8n,kind:3,region:'alpes',payloadJson:'{}'}]});
  assert.deepEqual(AdminSnapshot.decode(AdminSnapshot.encode(snapshot).finish()),snapshot);
});
