import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { BotClient } from './client.js';
import { percentile, readScenario, viewportFor } from './scenario.js';

const scenarioPath = process.argv[2] ?? 'scenarios/p0.toml';
const url = process.env.ORBIS_WS_URL ?? 'ws://127.0.0.1:8080/ws';
const scenario = readScenario(await readFile(scenarioPath,'utf8'));
const samples: number[] = [];
const failures: string[] = [];
const start = performance.now();
await Promise.all(Array.from({length:scenario.bots},async (_,bot)=>{
  let client: BotClient | undefined;
  try {
    client = await BotClient.open(url,scenario.timeoutMs);
    for (let command=0;command<scenario.commands;command++) {
      const viewport = bot===0 && command===0
        ? {center:{lat:0,lon:0},zoom:0,widthPx:1024,heightPx:768}
        : viewportFor(scenario.seed,bot,command);
      const started = performance.now();
      const response = await client.request({$case:'viewport',viewport});
      samples.push(performance.now()-started);
      assert.equal(response.kind?.$case,'snapshot');
      if (response.kind?.$case!=='snapshot') throw new Error('Expected snapshot');
      const entities = response.kind.snapshot.entities;
      assert.equal(new Set(entities.map(entity=>entity.id)).size,entities.length);
      if (bot===0 && command===0) assert.equal(entities.length,scenario.fullCount);
      else assert.ok(entities.length<100);
      for (const entity of entities) assert.ok(entity.pos && Number.isFinite(entity.pos.lat) && Number.isFinite(entity.pos.lon));
    }
    const invalid = await client.request({$case:'viewport',viewport:{...viewportFor(scenario.seed,bot,0),zoom:99}});
    assert.equal(invalid.kind?.$case,'result');
    if (invalid.kind?.$case!=='result') throw new Error('Expected rejection');
    assert.equal(invalid.kind.result.ok,false);
    assert.equal(invalid.kind.result.error,'invalid_viewport');
  } catch (error) { failures.push(`bot ${bot}: ${String(error)}`); }
  finally { client?.close(); }
}));
const p95 = samples.length ? percentile(samples,0.95) : Infinity;
const passed = failures.length===0 && samples.length===scenario.bots*scenario.commands && p95<=scenario.maxP95Ms;
console.log(JSON.stringify({scenario:scenarioPath,seed:scenario.seed,bots:scenario.bots,commands:samples.length,
  invalidCommandsChecked:scenario.bots-failures.length,p50Ms:samples.length?percentile(samples,0.5):null,
  p95Ms:Number.isFinite(p95)?p95:null,p99Ms:samples.length?percentile(samples,0.99):null,
  elapsedMs:performance.now()-start,failures,result:passed?'passed':'failed'},null,2));
if (!passed) process.exitCode=1;
