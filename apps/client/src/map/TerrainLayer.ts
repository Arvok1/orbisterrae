import { Application, Container, Graphics, Sprite, Texture } from 'pixi.js';
import { project, type Point } from './projection.js';

type Coordinate = [number, number];
type Polygon = Coordinate[][];
export interface TerrainData {
  version: number;
  rivers: {name: string; lines: Coordinate[][]}[];
  lakes: {name: string; polygons: Polygon[]}[];
  biomes: {biome: number; name: string}[];
  relief: [number, number, number][];
  symbols: [number, number, number][];
  stats: Record<string, number>;
}
const BASIS = 4096;

function xy([lon, lat]: Coordinate): [number, number] {
  const p = project({lon, lat});
  return [p.x * BASIS, p.y * BASIS];
}

/** Static real geographic geometry plus shared cartographic glyphs.
 * Every source feature is present at every zoom; only the viewport clips it.
 */
export class TerrainLayer extends Container {
  private symbols: {sprite: Sprite; mountain: boolean}[] = [];
  private ownedTextures: Texture[] = [];
  private previousSize = 0;

  constructor(app: Application, data: TerrainData, biomes: Texture) {
    super();
    const regions = new Sprite(biomes);
    regions.width = regions.height = BASIS;
    this.addChild(regions);

    const trees = this.texture(app, 'tree');
    const pines = this.texture(app, 'pine');
    const dunes = this.texture(app, 'dune');
    const mountains = this.texture(app, 'mountain');
    const snow = this.texture(app, 'snow');
    const hills = this.texture(app, 'hill');
    const landmarks = new Container();
    const entries = [
      ...data.symbols.map(([lon,lat,biome]) => ({lon,lat,texture:biome===13?dunes:biome===3||biome===5||biome===6?pines:trees,mountain:false})),
      ...data.relief.map(([lon,lat,height]) => ({lon,lat,texture:height>2800?snow:height>1200?mountains:hills,mountain:true})),
    ].sort((a,b) => b.lat-a.lat);
    for (const entry of entries) {
      const sprite = new Sprite(entry.texture);
      sprite.anchor.set(0.5, 0.8);
      sprite.position.set(...xy([entry.lon,entry.lat]));
      landmarks.addChild(sprite);
      this.symbols.push({sprite,mountain:entry.mountain});
    }
    this.addChild(landmarks);

    const water = new Graphics();
    for (const lake of data.lakes) {
      for (const polygon of lake.polygons) this.polygon(water,polygon,'#80b5c4');
    }
    for (const river of data.rivers) for (const line of river.lines) {
      let previous: [number,number] | null = null;
      for (const coordinate of line) {
        const point = xy(coordinate);
        if (!previous || Math.abs(point[0]-previous[0])>BASIS/2) water.moveTo(...point);
        else water.lineTo(...point);
        previous=point;
      }
      water.stroke({color:'#568ca3',pixelLine:true});
    }
    this.addChild(water);
    this.eventMode = 'none';
  }

  private polygon(graphics: Graphics, polygon: Polygon, color: string) {
    if (!polygon[0]?.length) return;
    graphics.poly(polygon[0].flatMap(xy)).fill(color);
    for (const hole of polygon.slice(1)) graphics.poly(hole.flatMap(xy)).cut();
  }

  private texture(app: Application, kind: string) {
    const g = new Graphics();
    if (kind==='tree') {
      g.ellipse(11,21,9,3).fill({color:'#56683b',alpha:0.2});
      g.rect(10,11,3,11).fill('#8a6940');
      g.circle(7,10,6).circle(15,10,6).circle(11,5,6).fill('#577f45').stroke({color:'#465e39',width:0.8});
      g.circle(7,7,3).fill('#91af5d');
    } else if (kind==='pine') {
      g.rect(10,14,2,9).fill('#8a6940');
      g.poly([11,0,3,12,6,12,0,19,22,19,16,12,19,12]).fill('#527764').stroke({color:'#3b5f4a',width:0.8});
      g.poly([11,2,6,11,11,9]).fill('#82a77f');
    } else if (kind==='dune') {
      g.moveTo(0,16).quadraticCurveTo(6,4,13,15).quadraticCurveTo(19,7,25,16).stroke({color:'#b89954',width:1.2});
      g.moveTo(5,19).quadraticCurveTo(12,13,21,20).stroke({color:'#c3a565',width:0.8});
    } else if (kind==='hill') {
      g.moveTo(0,19).quadraticCurveTo(11,-1,22,19).fill('#b3ac78').stroke({color:'#918b60',width:0.8});
      g.moveTo(1,18).quadraticCurveTo(9,2,11,7).lineTo(10,18).fill('#c9bd88');
    } else {
      g.poly([1,24,12,1,27,24]).fill('#a59678').stroke({color:'#746d56',width:1});
      g.poly([12,2,11,24,26,24]).fill('#8b826a');
      g.poly([12,2,6,13,10,11,13,14,16,11,20,14]).fill(kind==='snow'?'#f4eed8':'#d5c7a4');
    }
    const texture = app.renderer.generateTexture({target:g,resolution:2});
    g.destroy();
    this.ownedTextures.push(texture);
    return texture;
  }

  updateView(center: Point, size: number, width: number, height: number) {
    const scale = size/BASIS;
    this.position.set(width/2-center.x*size,height/2-center.y*size);
    this.scale.set(scale);
    if (size!==this.previousSize) {
      for (const {sprite,mountain} of this.symbols) {
        const pixels = mountain ? Math.max(8,Math.min(22,size/120)) : Math.max(5,Math.min(16,size/180));
        sprite.scale.set(pixels/sprite.texture.width/scale);
      }
      this.previousSize=size;
    }
  }

  override destroy(options?: Parameters<Container['destroy']>[0]) {
    super.destroy(options);
    for (const texture of this.ownedTextures) texture.destroy(true);
    this.symbols=[];
  }
}
