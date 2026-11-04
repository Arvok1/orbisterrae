export interface Coordinate { lat: number; lon: number }
export interface Point { x: number; y: number }
export const MAX_LATITUDE = 85.0511287798066;

export function project({lat, lon}: Coordinate): Point {
  const r = Math.max(-MAX_LATITUDE, Math.min(MAX_LATITUDE, lat)) * Math.PI / 180;
  return { x: (lon + 180) / 360, y: (1 - Math.asinh(Math.tan(r)) / Math.PI) / 2 };
}

export function unproject({x, y}: Point): Coordinate {
  return { lon: ((x % 1 + 1) % 1) * 360 - 180,
    lat: Math.atan(Math.sinh(Math.PI * (1 - 2 * Math.max(0, Math.min(1, y))))) * 180 / Math.PI };
}

export interface Tile { x: number; y: number; z: number }
export function visibleTiles(center: Point, worldSize: number, width: number, height: number): Tile[] {
  const z = Math.max(0, Math.min(6, Math.floor(Math.log2(worldSize / 256))));
  const n = 2 ** z;
  const left = Math.max(0, Math.floor((center.x - width / worldSize / 2) * n));
  const right = Math.min(n - 1, Math.floor((center.x + width / worldSize / 2) * n));
  const top = Math.max(0, Math.floor((center.y - height / worldSize / 2) * n));
  const bottom = Math.min(n - 1, Math.floor((center.y + height / worldSize / 2) * n));
  const tiles: Tile[] = [];
  for (let x = left; x <= right; x++) for (let y = top; y <= bottom; y++) tiles.push({x, y, z});
  return tiles;
}
