import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { readScenario, percentile, viewportFor } from '../dist/scenario.js';

const source = readFileSync(new URL('../../../scenarios/p0.toml',import.meta.url),'utf8');
test('scenario rejects invalid counts and latency limits',()=>{
  assert.equal(readScenario(source).bots,100);
  assert.throws(()=>readScenario(source.replace('bots = 100','bots = -1')),/run.bots/);
  assert.throws(()=>readScenario(source.replace('timeout_ms = 5000','timeout_ms = "oops"')),/timeout_ms/);
});
test('seeded viewport tour repeats exactly and stays in protocol bounds',()=>{
  const tour = seed=>Array.from({length:1000},(_,id)=>viewportFor(seed,id%100,Math.floor(id/100)));
  const first=tour(42);
  assert.deepEqual(first,tour(42));
  assert.notDeepEqual(first,tour(43));
  for (const view of first) {
    assert.ok(view.center.lat>=-70 && view.center.lat<=70);
    assert.ok(view.center.lon>=-180 && view.center.lon<=180);
  }
});
test('nearest-rank percentiles do not mutate samples and retain the tail',()=>{
  const values=[100,1,5,2,3];
  assert.equal(percentile(values,0.5),3);
  assert.equal(percentile(values,0.95),100);
  assert.deepEqual(values,[100,1,5,2,3]);
  assert.throws(()=>percentile([],0.95));
});
