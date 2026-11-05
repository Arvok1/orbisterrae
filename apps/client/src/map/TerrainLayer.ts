import { Application, ColorMatrixFilter, Container, Graphics, Sprite, Texture } from 'pixi.js';
import { project, type Point } from './projection.js';
import type { Coordinate as Location } from './projection.js';
import { decoration,foliageBiome } from './scenery.js';

type Coordinate = [number, number];
type Polygon = Coordinate[][];
export interface TerrainData {
  version: number;
  rivers: {name: string;rank:number;width:number;lines: Coordinate[][]}[];
  lakes: {name: string; polygons: Polygon[]}[];
  biomes: {biome: number; name: string}[];
  stats: Record<string, number>;
}
const BASIS = 4096;

function xy([lon, lat]: Coordinate): [number, number] {
  const p = project({lon, lat});
  return [p.x * BASIS, p.y * BASIS];
}

/** Continuous, data-anchored landscape for all three LODs. */
export class TerrainLayer extends Container {
  private ownedTextures: Texture[] = [];
  private closeScenery=new Container();
  private detailSprites:Sprite[]=[];
  private biomePixels:Uint8ClampedArray|null=null;
  private detailTextures:Record<string,Texture>={};
  private rivers=new Graphics();
  private lakes=new Graphics();
  private relief:Sprite;
  private lastHydroScale=0;
  private lastLod=-1;

  constructor(private app: Application,private data: TerrainData,biomes: Texture,reliefTexture:Texture) {
    super();
    const regions = new Sprite(biomes);
    regions.width = regions.height = BASIS;
    regions.alpha=0.92;
    regions.tint='#6d6455';
    const grim = new ColorMatrixFilter();
    grim.matrix = [
      0.30, 0.32, 0.08, 0, -0.04,
      0.24, 0.27, 0.07, 0, -0.03,
      0.17, 0.20, 0.06, 0, -0.02,
      0, 0, 0, 1, 0,
    ];
    regions.filters=[grim];
    this.relief=new Sprite(reliefTexture);
    this.relief.width=this.relief.height=BASIS;
    this.relief.blendMode='multiply';
    this.relief.alpha=0.5;
    this.detailTextures={forest:this.texture('forest'),pineForest:this.texture('pineForest'),dunes:this.texture('dunes'),autumnForest:this.texture('autumnForest')};
    const resource:unknown=biomes.source.resource;
    if (resource instanceof HTMLImageElement || resource instanceof ImageBitmap || resource instanceof HTMLCanvasElement) {
      const canvas=document.createElement('canvas');canvas.width=canvas.height=512;
      const context=canvas.getContext('2d');
      if (context) {context.imageSmoothingEnabled=true;context.drawImage(resource,0,0,512,512);this.biomePixels=context.getImageData(0,0,512,512).data;}
    }
    this.addChild(regions,this.relief,this.rivers,this.lakes,this.closeScenery);
    this.renderHydro(1,0);
    this.eventMode = 'none';
  }

  private renderHydro(scale:number,lod:number) {
    this.rivers.clear();this.lakes.clear();
    const widthFactor=[0.38,0.68,1][lod]??1;
    for (const river of this.data.rivers) for (const line of river.lines) {
      let previous: [number,number] | null = null;
      for (const coordinate of line) {
        const point = xy(coordinate);
        if (!previous || Math.abs(point[0]-previous[0])>BASIS/2) this.rivers.moveTo(...point);
        else this.rivers.lineTo(...point);
        previous=point;
      }
      const pixels=Math.max(0.65,river.width*widthFactor);
      this.rivers.stroke({color:'#4f6a70',width:pixels/scale,alpha:0.92});
    }
    for (const lake of this.data.lakes) for (const polygon of lake.polygons) {
      if (!polygon[0]?.length) continue;
      this.lakes.poly(polygon[0].flatMap(xy)).fill('#38505a');
      for (const hole of polygon.slice(1)) this.lakes.poly(hole.flatMap(xy)).cut();
      this.lakes.poly(polygon[0].flatMap(xy)).stroke({color:'#243a42',width:1.15/scale,alpha:0.9});
    }
    this.lastHydroScale=scale;this.lastLod=lod;
  }

  private texture(kind:string) {
    const g = new Graphics();
    if(kind==='dunes') {
      g.ellipse(60,32,58,22).fill({color:'#5d4e34',alpha:0.7});
      for(let row=0;row<5;row++) g.moveTo(6,18+row*8).bezierCurveTo(34,1+row*8,72,38+row*3,116,16+row*7).stroke({color:'#7a643c',width:2,alpha:0.55});
    } else {
      const autumn=kind==='autumnForest',pine=kind==='pineForest';
      const dark=autumn?'#4a3c28':pine?'#25302d':'#2c3028',middle=autumn?'#6d5530':pine?'#33433b':'#3c4034',light=autumn?'#8a6a38':pine?'#43594f':'#4a4f3e';
      g.ellipse(64,38,60,27).fill({color:dark,alpha:0.4});
      const crowns=[[14,35,18,13],[30,24,23,17],[53,31,25,18],[76,20,24,16],[96,34,26,18],[43,46,28,17],[78,45,30,18]];
      for(const [x,y,rx,ry] of crowns) g.ellipse(x,y,rx,ry).fill(middle);
      for(const [x,y,rx,ry] of crowns.filter((_,index)=>index%2===0)) g.ellipse(x-3,y-5,rx*0.6,ry*0.5).fill({color:light,alpha:0.72});
    }
    const texture=this.app.renderer.generateTexture({target:g,resolution:2});g.destroy();this.ownedTextures.push(texture);return texture;
  }

  updateView(center: Point, size: number, width: number, height: number,clearings:Location[]=[]) {
    const scale = size/BASIS,zoom=Math.log2(size/256),lod=zoom<8?0:zoom<15?1:2;
    this.position.set(width/2-center.x*size,height/2-center.y*size);
    this.scale.set(scale);
    if(lod!==this.lastLod||!this.lastHydroScale||Math.max(scale/this.lastHydroScale,this.lastHydroScale/scale)>1.25) this.renderHydro(scale,lod);
    this.updateScenery(center,size,width,height,clearings);
  }

  private updateScenery(center:Point,size:number,width:number,height:number,clearings:Location[]) {
    const zoom=Math.log2(size/256);this.closeScenery.visible=zoom>=8&&this.biomePixels!==null;
    if (!this.closeScenery.visible) return;
    this.closeScenery.alpha=Math.min(0.92,(zoom-8)/3);
    const level=Math.min(17,Math.max(1,Math.floor(Math.log2(size/100)))),n=2**level,scale=size/BASIS;
    const left=Math.floor((center.x-width/size/2)*n)-1,right=Math.ceil((center.x+width/size/2)*n)+1;
    const top=Math.floor((center.y-height/size/2)*n)-1,bottom=Math.ceil((center.y+height/size/2)*n)+1;
    const towns=clearings.map(project);let used=0;
    for (let y=top;y<=bottom;y++) for (let x=left;x<=right;x++) {
      const {point,variant}=decoration(x,y,level,42);
      if (variant%3===1||point.x<0||point.x>=1||point.y<0||point.y>=1) continue;
      if (towns.some(town=>Math.hypot(town.x-point.x,town.y-point.y)*size<130)) continue;
      const index=(Math.floor(point.y*512)*512+Math.floor(point.x*512))*4,pixels=this.biomePixels!;
      const biome=foliageBiome(pixels[index],pixels[index+1],pixels[index+2],pixels[index+3]);
      if (!biome) continue;
      const texture=biome===13?this.detailTextures.dunes:[3,5,6].includes(biome)?this.detailTextures.pineForest:biome===4&&variant%3===0?this.detailTextures.autumnForest:this.detailTextures.forest;
      let sprite=this.detailSprites[used++];
      if (!sprite) {sprite=new Sprite(texture);sprite.anchor.set(0.5);this.closeScenery.addChild(sprite);this.detailSprites.push(sprite);}
      sprite.texture=texture;sprite.visible=true;sprite.position.set(point.x*BASIS,point.y*BASIS);
      const pixelsWide=120+variant%90;sprite.scale.set(pixelsWide/texture.width/scale);
      sprite.rotation=((variant%9)-4)*0.025;
    }
    for(let index=used;index<this.detailSprites.length;index++)this.detailSprites[index]!.visible=false;
  }

  override destroy(options?: Parameters<Container['destroy']>[0]) {
    super.destroy(options);for (const texture of this.ownedTextures) texture.destroy(true);
  }
}
