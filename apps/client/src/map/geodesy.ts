import type { Coordinate } from './projection.js';

/** Single earth reference for rendering, picking and server-facing coordinates. */
export const EARTH_RADIUS_M = 6_371_000;
const DEG = Math.PI / 180;

export type UnitVector = {x:number;y:number;z:number};
export type LocalMeters = {east:number;north:number};

export function latLonToUnit({lat,lon}:Coordinate):UnitVector {
  const phi=lat*DEG,lambda=lon*DEG;
  return {x:Math.cos(phi)*Math.sin(lambda),y:Math.sin(phi),z:Math.cos(phi)*Math.cos(lambda)};
}

export function unitToLatLon({x,y,z}:UnitVector):Coordinate {
  const length=Math.hypot(x,y,z)||1;
  return {lat:Math.asin(y/length)/DEG,lon:Math.atan2(x,z)/DEG};
}

/** Equirectangular local tangent approximation, accurate for the 2.56 km city patch. */
export function localMetersToLatLon(origin:Coordinate,local:LocalMeters):Coordinate {
  const lat=origin.lat+local.north/EARTH_RADIUS_M/DEG;
  const lon=origin.lon+local.east/(EARTH_RADIUS_M*Math.max(.01,Math.cos(origin.lat*DEG)))/DEG;
  return {lat,lon:((lon+540)%360)-180};
}

export function latLonToLocalMeters(origin:Coordinate,point:Coordinate):LocalMeters {
  const deltaLon=(((point.lon-origin.lon+540)%360)-180)*DEG;
  return {east:deltaLon*EARTH_RADIUS_M*Math.cos(origin.lat*DEG),north:(point.lat-origin.lat)*DEG*EARTH_RADIUS_M};
}
