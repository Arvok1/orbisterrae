import { parse, TomlDate } from 'smol-toml';
import type { SetViewport } from '@orbis/protocol';

export interface Scenario {
  seed: number;
  bots: number;
  commands: number;
  timeoutMs: number;
  maxP95Ms: number;
  fullCount: number;
}

export function readScenario(source: string): Scenario {
  const data = parse(source);
  function positive(section: string, key: string, maximum: number): number {
    const table = data[section];
    const value = table !== null && typeof table === 'object' && !Array.isArray(table) && !(table instanceof TomlDate) ? table[key] : undefined;
    if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 1 || value > maximum) {
      throw new Error(`Invalid scenario ${section}.${key}`);
    }
    return value;
  }
  return {
    seed:positive('run','seed',0xffffffff), bots:positive('run','bots',1000),
    commands:positive('run','commands_per_bot',10000), timeoutMs:positive('run','timeout_ms',60000),
    maxP95Ms:positive('assertions','max_command_latency_p95_ms',60000),
    fullCount:positive('assertions','expected_full_snapshot_entities',100000),
  };
}

export function viewportFor(seed: number, bot: number, command: number): SetViewport {
  let value = (seed ^ Math.imul(bot+1, 0x9e3779b9) ^ Math.imul(command+1, 0x85ebca6b)) >>> 0;
  value ^= value << 13; value ^= value >>> 17; value ^= value << 5;
  const x = value >>> 0;
  return {center:{lat:(x%14000)/100-70,lon:((x>>>8)%36000)/100-180}, zoom:6,widthPx:640,heightPx:480};
}

export function percentile(samples: number[], ratio: number): number {
  if (!samples.length || ratio <= 0 || ratio > 1) throw new Error('Invalid percentile inputs');
  const ordered = [...samples].sort((a,b)=>a-b);
  return ordered[Math.ceil(ordered.length*ratio)-1];
}
