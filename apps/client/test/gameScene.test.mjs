import assert from 'node:assert/strict';
import test from 'node:test';
import { rulerPosition,outerEdges } from '../dist/map/gameScene.js';
test('ruler interpolation uses server segment endpoints and clamps time',()=>{
  const ruler={pos:{lat:0,lon:179},journey:{from:{lat:0,lon:179},to:{lat:2,lon:-179},tStartMs:100},journeyDueMs:200};
  assert.deepEqual(rulerPosition(ruler,50),{lat:0,lon:179});
  assert.deepEqual(rulerPosition(ruler,150),{lat:1,lon:-180});
  assert.deepEqual(rulerPosition(ruler,250),{lat:2,lon:-179});
});
test('territory outline removes shared cell edges without changing ownership',()=>{
  const a={lat:0,lon:0},b={lat:0,lon:1},c={lat:1,lon:1},d={lat:1,lon:0},e={lat:0,lon:2},f={lat:1,lon:2};
  const cells=[{boundary:[a,b,c,d]},{boundary:[b,e,f,c]}];
  assert.equal(outerEdges(cells).length,6);
  assert.equal(outerEdges([]).length,0);
  assert.deepEqual(cells[0].boundary,[a,b,c,d]);
});
