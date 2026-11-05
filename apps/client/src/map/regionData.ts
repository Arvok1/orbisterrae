import type { Coordinate } from './projection.js';
import { EARTH_RADIUS_M } from './geodesy.js';

export function regionalSpan(zoom:number):number {
  return Math.max(400,Math.min(8_000_000,2*Math.PI*EARTH_RADIUS_M*2**(9-Math.min(29,zoom))));
}

export function pointInPolygon(point:[number,number],ring:[number,number][]):boolean {
  let inside=false;
  for(let i=0,j=ring.length-1;i<ring.length;j=i++) {
    const a=ring[i],b=ring[j];
    if(a&&b&&((a[1]>point[1])!==(b[1]>point[1]))&&point[0]<(b[0]-a[0])*(point[1]-a[1])/(b[1]-a[1])+a[0])inside=!inside;
  }
  return inside;
}

export function inLake(point:[number,number],polygons:[number,number][][]):boolean {
  return polygons.some(ring=>pointInPolygon(point,ring));
}

export function mercatorSample(position:Coordinate):{u:number;v:number} {
  const latitude=Math.max(-85.05112878,Math.min(85.05112878,position.lat))*Math.PI/180;
  return {u:((position.lon+180)/360%1+1)%1,v:Math.max(0,Math.min(1,.5-Math.asinh(Math.tan(latitude))/(2*Math.PI)))};
}
