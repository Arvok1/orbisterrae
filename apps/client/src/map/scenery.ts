import type { Point } from './projection.js';

/** Anchored illustrative foliage, unrelated to gameplay resources or collisions. */
export function decoration(x:number,y:number,level:number,seed:number): {point:Point;variant:number} {
  let hash=(Math.imul(x,73856093)^Math.imul(y,19349663)^seed)>>>0;
  hash^=hash<<13;hash^=hash>>>17;hash^=hash<<5;hash>>>=0;
  const n=2**level;
  return {point:{x:(x+0.2+(hash%61)/100)/n,y:(y+0.2+((hash>>>8)%61)/100)/n},variant:hash};
}

const palette:Record<number,number>={0x89ae65:1,0xb0ba70:2,0x86a767:3,0xa7b96b:4,0x91ab72:5,0x92afa0:6,0xe7ce8d:13,0x77a98a:14};
export function foliageBiome(r:number,g:number,b:number,alpha:number):number {
  return alpha>=128?palette[(r<<16)|(g<<8)|b]??0:0;
}
