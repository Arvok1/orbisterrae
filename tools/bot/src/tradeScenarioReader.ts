import { parse, TomlDate } from 'smol-toml';

export interface TradeScenario {
  seed: number;
  concurrency: number;
  timeoutMs: number;
  minimumHubs: number;
  initialHubMetal: number;
  commodity: string;
  quantity: number;
}

export function readTradeScenario(source: string): TradeScenario {
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
  function text(section: string, key: string): string {
    const value = table(section)[key];
    if (typeof value !== 'string' || value.length === 0 || value.length > 48) {
      throw new Error(`Invalid scenario ${section}.${key}`);
    }
    return value;
  }
  return {
    seed: integer('run', 'seed', 1, 0xffffffff),
    concurrency: integer('run', 'concurrency', 1, 100),
    timeoutMs: integer('run', 'timeout_ms', 1000, 120000),
    minimumHubs: integer('assertions', 'minimum_hubs', 1, 1000),
    initialHubMetal: integer('assertions', 'initial_hub_metal', 1, 1_000_000_000),
    commodity: text('assertions', 'commodity'),
    quantity: integer('assertions', 'quantity', 1, 100000),
  };
}
