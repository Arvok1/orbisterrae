import assert from 'node:assert/strict';
import test from 'node:test';
import { regionalSpan, inLake, mercatorSample } from '../dist/map/regionData.js';

test('close zoom keeps shrinking to a local metre view',()=>{
  assert.ok(regionalSpan(16)<regionalSpan(12));
  assert.ok(regionalSpan(20)<regionalSpan(16));
  assert.equal(regionalSpan(29),400);
});

test('lake mask rejects a river point inside the water polygon',()=>{
  const lake=[[[-2,0],[2,0],[2,2],[-2,2]]];
  assert.equal(inLake([0,1],lake),true);
  assert.equal(inLake([3,1],lake),false);
});

test('Mercator sampler remains finite at the poles',()=>{
  for(const lat of [-90,0,90]) {const {u,v}=mercatorSample({lat,lon:10});assert.ok(u>=0&&u<1);assert.ok(v>=0&&v<=1);}
});
