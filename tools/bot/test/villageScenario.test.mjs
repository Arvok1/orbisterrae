import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { readVillageScenario } from '../dist/villageScenario.js';
const source=readFileSync(new URL('../../../scenarios/s1_1.toml',import.meta.url),'utf8');
test('village scenario checks bot counts, concurrency and pending transfers',()=>{
  assert.equal(readVillageScenario(source).bots,100);
  assert.throws(()=>readVillageScenario(source.replace('concurrency = 10','concurrency = 101')));
  assert.throws(()=>readVillageScenario(source.replace('active_villages = 100','active_villages = 99')));
  assert.throws(()=>readVillageScenario(source.replace('maximum_pending_handoffs = 0','maximum_pending_handoffs = 1')));
});
