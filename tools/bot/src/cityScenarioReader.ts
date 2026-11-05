import { parse, TomlDate } from 'smol-toml';

export interface CityScenario {
  seed: number;
  bots: number;
  concurrency: number;
  timeoutMs: number;
  tickMs: number;
  cities: number;
  minimumDefenseDelta: number;
  minimumGrowthDelta: number;
}

export function readCityScenario(source: string): CityScenario {
  const data = parse(source);
  function table(section: string): Record<string, unknown> {
    const value = data[section];
    if (value === null || typeof value !== 'object' || Array.isArray(value) || value instanceof TomlDate) {
      throw new Error(`Missing scenario section ${section}`);
    }
    return value as Record<string, unknown>;
  }
  function integer(section: string, key: string, min: number, max: number): number {
    const value = table(section)[key];
    if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < min || value > max) {
      throw new Error(`Invalid scenario ${section}.${key}`);
    }
    return value;
  }
  function number(section: string, key: string): number {
    const value = table(section)[key];
    if (typeof value !== 'number' || !Number.isFinite(value) || value <= 0) {
      throw new Error(`Invalid scenario ${section}.${key}`);
    }
    return value;
  }
  const scenario = {
    seed: integer('run', 'seed', 1, 0xffffffff),
    bots: integer('run', 'bots', 1, 1000),
    concurrency: integer('run', 'concurrency', 1, 100),
    timeoutMs: integer('run', 'timeout_ms', 1000, 120000),
    tickMs: integer('run', 'tick_ms', 100, 10000),
    cities: integer('assertions', 'cities', 1, 1000),
    minimumDefenseDelta: number('assertions', 'minimum_defense_delta'),
    minimumGrowthDelta: integer('assertions', 'minimum_growth_delta', 1, 100000),
  };
  if (scenario.bots !== scenario.cities || scenario.concurrency > scenario.bots) {
    throw new Error('Inconsistent city scenario');
  }
  return scenario;
}
