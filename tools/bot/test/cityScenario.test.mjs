import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { readCityScenario } from '../dist/cityScenarioReader.js';

const source = readFileSync(new URL('../../../scenarios/s1_2.toml', import.meta.url), 'utf8');

test('city scenario validates bots, tick and acceptance thresholds', () => {
  const scenario = readCityScenario(source);
  assert.equal(scenario.bots, 3);
  assert.equal(scenario.tickMs, 250);
  assert.throws(() => readCityScenario(source.replace('tick_ms = 250', 'tick_ms = 50')));
  assert.throws(() => readCityScenario(source.replace('minimum_growth_delta = 4', 'minimum_growth_delta = 0')));
  assert.throws(() => readCityScenario(source.replace('cities = 3', 'cities = 4')));
});
