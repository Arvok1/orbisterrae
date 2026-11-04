import { parse,TomlDate } from 'smol-toml';
export function readVillageScenario(source:string) {
  const data=parse(source);
  function integer(section:string,key:string,min:number,max:number) {
    const table=data[section];
    const value=table&&typeof table==='object'&&!Array.isArray(table)&&!(table instanceof TomlDate)?table[key]:undefined;
    if(typeof value!=='number'||!Number.isSafeInteger(value)||value<min||value>max) throw new Error(`Invalid scenario ${section}.${key}`);
    return value;
  }
  const result={seed:integer('run','seed',1,0xffffffff),bots:integer('run','bots',1,1000),concurrency:integer('run','concurrency',1,100),
    timeoutMs:integer('run','timeout_ms',1000,60000),villages:integer('assertions','active_villages',1,1000),pendingHandoffs:integer('assertions','maximum_pending_handoffs',0,0)};
  if(result.bots!==result.villages||result.concurrency>result.bots) throw new Error('Inconsistent village scenario');
  return result;
}
