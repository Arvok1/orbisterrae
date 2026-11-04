import type { RulerState, TerritoryCell } from '@orbis/protocol';
import type { Coordinate } from './projection.js';

/** Presentation-only interpolation between server endpoints. */
export function rulerPosition(ruler:RulerState,nowMs:number): Coordinate {
  const from=ruler.journey?.from,to=ruler.journey?.to;
  if (!from || !to) return ruler.pos??{lat:0,lon:0};
  const t=Math.max(0,Math.min(1,(nowMs-ruler.journey!.tStartMs)/Math.max(1,ruler.journeyDueMs-ruler.journey!.tStartMs)));
  const delta=((to.lon-from.lon+540)%360)-180;
  return {lat:from.lat+(to.lat-from.lat)*t,lon:((from.lon+delta*t+540)%360)-180};
}
export function outerEdges(cells:TerritoryCell[]): [Coordinate,Coordinate][] {
  const edges=new Map<string,{count:number;pair:[Coordinate,Coordinate]}>();
  const key=(p:Coordinate)=>`${p.lon.toFixed(8)},${p.lat.toFixed(8)}`;
  for (const cell of cells) for (let i=0;i<cell.boundary.length;i++) {
    const a=cell.boundary[i],b=cell.boundary[(i+1)%cell.boundary.length];
    const id=[key(a),key(b)].sort().join('|');
    const edge=edges.get(id);
    if (edge) edge.count++;else edges.set(id,{count:1,pair:[a,b]});
  }
  return [...edges.values()].filter(edge=>edge.count===1).map(edge=>edge.pair);
}
