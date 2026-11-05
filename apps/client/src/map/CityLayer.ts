import { Container, Graphics, type PointData } from 'pixi.js';
import type { CitySnapshot } from '@orbis/protocol';
import { project, type Point } from './projection.js';

const WORLD_METERS=40_075_017;
const CELL_METERS=10;

export function decodeRle(encoded:Uint8Array,expected:number) {
  const result:number[]=[];
  if (encoded.length%3!==0) throw new Error('invalid_rle');
  for(let offset=0;offset<encoded.length;offset+=3) {
    const value=encoded[offset]??0,run=(encoded[offset+1]??0)|((encoded[offset+2]??0)<<8);
    if (!run||result.length+run>expected) throw new Error('invalid_rle');
    for(let index=0;index<run;index++) result.push(value);
  }
  if(result.length!==expected) throw new Error('invalid_rle');
  return result;
}

const iso=(x:number,y:number):[number,number]=>[x-y,(x+y)/2];
const diamond=(x:number,y:number)=>{
  const [topX,topY]=iso(x,y),[rightX,rightY]=iso(x+1,y),[bottomX,bottomY]=iso(x+1,y+1),[leftX,leftY]=iso(x,y+1);
  return [topX,topY,rightX,rightY,bottomX,bottomY,leftX,leftY];
};

/** Pixi-only projection of authoritative city state; it performs no game decisions. */
export class CityLayer extends Container {
  private snapshot:CitySnapshot|null=null;
  private ground=new Graphics();
  private structures=new Container();
  private atmosphere=new Graphics();
  private targetRotation=0;
  private reduced=matchMedia('(prefers-reduced-motion: reduce)').matches;
  private roads:number[]=[];
  private zones:number[]=[];

  constructor() {
    super();
    this.addChild(this.ground,this.structures,this.atmosphere);
    this.eventMode='none';
    this.visible=false;
  }

  sync(snapshot:CitySnapshot|null,rotation:number) {
    const changed=this.snapshot!==snapshot;
    this.snapshot=snapshot;
    this.targetRotation=rotation*Math.PI/2;
    if(this.reduced) this.rotation=this.targetRotation;
    if(changed) this.rebuild();
  }

  frame(deltaMs:number) {
    if(this.reduced) return;
    let difference=((this.targetRotation-this.rotation+Math.PI*3)%(Math.PI*2))-Math.PI;
    if(Math.abs(difference)<0.001) this.rotation=this.targetRotation;
    else this.rotation+=difference*Math.min(1,deltaMs/180);
  }

  updateView(center:Point,size:number,width:number,height:number) {
    const snapshot=this.snapshot;
    if(!snapshot) {this.visible=false;return;}
    const zoom=Math.log2(size/256),city=projectCity(snapshot);
    this.visible=zoom>=15;
    this.alpha=Math.min(1,Math.max(0,(zoom-15)/1.5));
    this.position.set(width/2+(city.x-center.x)*size,height/2+(city.y-center.y)*size);
    const pixelsPerCell=size*CELL_METERS/WORLD_METERS;
    this.scale.set(Math.max(1,pixelsPerCell));
  }

  cellAt(screen:PointData):{x:number;y:number}|null {
    if(!this.visible||!this.snapshot) return null;
    const local=this.toLocal(screen);
    const x=Math.floor(local.y+local.x/2),y=Math.floor(local.y-local.x/2);
    return x>=0&&y>=0&&x<this.snapshot.width&&y<this.snapshot.height?{x,y}:null;
  }

  roadAt(x:number,y:number) {return this.roads[y*(this.snapshot?.width??0)+x]??0;}
  zoneAt(x:number,y:number) {return this.zones[y*(this.snapshot?.width??0)+x]??0;}

  private rebuild() {
    this.ground.clear();this.atmosphere.clear();this.structures.removeChildren().forEach(child=>child.destroy());
    const city=this.snapshot;if(!city)return;
    this.pivot.set(0,city.height/2);
    const count=city.width*city.height;
    try {this.roads=decodeRle(city.roadsRle,count);this.zones=decodeRle(city.zonesRle,count);} catch {this.visible=false;return;}
    const zoneColors=['#3a3328','#40372b','#3d402b','#3b3027','#423a2b','#343028'];
    for(let y=0;y<city.height;y++) for(let x=0;x<city.width;x++) {
      const index=y*city.width+x,road=this.roads[index]??0,zone=this.zones[index]??0;
      this.ground.poly(diamond(x,y)).fill({color:road?'#241e17':zoneColors[zone]??zoneColors[0],alpha:road?1:0.9}).stroke({color:'#15100c',width:0.045,alpha:0.32});
      if((x+y)%12===0&&!road) this.ground.circle(...iso(x+0.5,y+0.5),0.08).fill({color:'#4a4034',alpha:0.35});
    }
    const definitions=new Map(city.definitions.map(definition=>[definition.id,definition]));
    const buildings=[...city.buildings].sort((a,b)=>(a.x+a.y)-(b.x+b.y));
    for(const building of buildings) {
      const definition=definitions.get(building.buildingId);if(!definition)continue;
      const rotated=building.orientation%2===1;
      const width=rotated?definition.height:definition.width,height=rotated?definition.width:definition.height;
      const structure=new Graphics(),base=diamond(building.x,building.y);
      const [cx,cy]=iso(building.x+width/2,building.y+height/2);
      const footprint=[...iso(building.x,building.y),...iso(building.x+width,building.y),...iso(building.x+width,building.y+height),...iso(building.x,building.y+height)];
      if(!building.complete) {
        structure.poly(footprint).fill({color:'#3b342a',alpha:0.65}).stroke({color:'#241e17',width:0.12});
        for(let level=0;level<3;level++) structure.moveTo(cx-width/2,cy-level*0.45).lineTo(cx+width/2,cy-level*0.45).stroke({color:'#241e17',width:0.08});
      } else if(definition.visualRole==='field') {
        structure.poly(footprint).fill('#3d402b').stroke({color:'#2b2a1c',width:0.08});
        for(let row=0;row<Math.max(2,height*2);row++) structure.moveTo(cx-width/2,cy-height/4+row*0.18).lineTo(cx+width/2,cy-height/4+row*0.18).stroke({color:'#5a5a3a',width:0.05});
      } else if(definition.visualRole==='wall') {
        structure.poly(footprint).fill('#45403a').stroke({color:'#241e17',width:0.1});
      } else {
        const buildingHeight=Math.max(0.8,Math.min(2.4,(width+height)/3));
        structure.poly(footprint).fill('#b7a98a').stroke({color:'#241e17',width:0.16});
        structure.poly([cx-width/1.7,cy-buildingHeight/2,cx,cy-buildingHeight,cx+width/1.7,cy-buildingHeight/2,cx,cy-buildingHeight/3]).fill(definition.visualRole==='tower'?'#3a2f28':'#191510').stroke({color:'#0e0b08',width:0.16});
        structure.rect(cx-0.12,cy-buildingHeight/2,0.24,0.4).fill('#2a241c');
      }
      structure.alpha=building.complete?1:0.85;
      this.structures.addChild(structure);
      if(building.complete&&['forge','workshop','smokehouse'].includes(definition.visualRole)) {
        this.atmosphere.circle(cx+0.2,cy-1.4,0.16).circle(cx+0.32,cy-1.75,0.22).fill({color:'#151310',alpha:0.4});
      }
      void base;
    }
    const fire=city.events.some(event=>event.kind==='fire'),famine=city.events.some(event=>event.kind==='famine');
    if(fire) this.atmosphere.circle(0,city.height/2,2.2).fill({color:'#b8431f',alpha:0.5});
    if(famine) this.atmosphere.rect(-city.width,0,city.width*2,city.height).fill({color:'#5b4327',alpha:0.18});
  }
}

function projectCity(snapshot:CitySnapshot):Point {
  // Position is supplied by Atlas because CitySnapshot intentionally contains simulation state only.
  const marker=(snapshot as CitySnapshot&{_projected?:Point})._projected;
  return marker??{x:-10,y:-10};
}

export function anchorCity(snapshot:CitySnapshot,point:Point) {
  (snapshot as CitySnapshot&{_projected?:Point})._projected=point;
}
