export type Lod='global'|'regional'|'city';

export function lodForZoom(zoom:number):Lod {return zoom>=15?'city':zoom>=8?'regional':'global';}
