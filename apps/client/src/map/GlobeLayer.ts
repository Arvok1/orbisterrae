import {
  AmbientLight, BoxGeometry, CanvasTexture, Color, ConeGeometry, CylinderGeometry,
  DirectionalLight, Group, Raycaster, Mesh, MeshStandardMaterial, PerspectiveCamera,
  Scene, SphereGeometry, Vector2,
  SRGBColorSpace, Vector3, WebGLRenderer,
} from 'three';
import type { AdminSnapshot, WorldSnapshot } from '@orbis/protocol';
import type { Point } from './projection.js';
import type { Coordinate } from './projection.js';

type Pin = {key:string;pos:Coordinate;kind:'city'|'ruler'|'unit'};

/** Full-screen illustrated globe with a continuous camera transition into displaced terrain. */
export class GlobeLayer {
  readonly canvas: HTMLCanvasElement;
  private renderer: WebGLRenderer;
  private scene = new Scene();
  private camera = new PerspectiveCamera(36, 1, 0.01, 40);
  private world = new Group();
  private pins = new Group();
  private pinNodes = new Map<string,Group>();
  private earth: Mesh | null = null;
  private readonly raycaster = new Raycaster();
  private readonly pinGeometry = new SphereGeometry(.006,12,8);
  private readonly rulerGeometry = new SphereGeometry(.012,12,8);
  private readonly pinMaterial = new MeshStandardMaterial({color:'#a75b3d',roughness:.7});
  private readonly rulerMaterial = new MeshStandardMaterial({color:'#dbc080',roughness:.7});
  private readonly cityBlock = new BoxGeometry(.014,.012,.014);
  private readonly roof = new ConeGeometry(.012,.012,4);
  private readonly soldierBody = new CylinderGeometry(.004,.007,.018,6);
  private readonly soldierHead = new SphereGeometry(.004,8,6);
  private readonly pennant = new BoxGeometry(.001,.018,.001);
  private readonly cityRoofMaterial = new MeshStandardMaterial({color:'#45352c',roughness:.9});
  private readonly cityWallMaterial = new MeshStandardMaterial({color:'#b49b73',roughness:.95});
  private readonly troopMaterial = new MeshStandardMaterial({color:'#68453c',roughness:.8});
  private readonly ironMaterial = new MeshStandardMaterial({color:'#8e8063',roughness:.65});
  private frame = 0;
  private disposed = false;
  private pinsKey = '';
  private center = new Vector3(0,0,1);
  private progress = 0;

  constructor(host: HTMLElement, biomeUrl: string, reliefUrl: string) {
    this.renderer = new WebGLRenderer({alpha:false,antialias:true,powerPreference:'high-performance'});
    this.renderer.setPixelRatio(Math.min(devicePixelRatio,1.5));
    this.renderer.setClearColor('#171918');
    this.renderer.outputColorSpace = SRGBColorSpace;
    this.canvas = this.renderer.domElement;
    this.canvas.className = 'globe-canvas';
    this.scene.background = new Color('#171918');
    this.scene.add(new AmbientLight('#c5b89f', 1.6));
    const sun = new DirectionalLight('#f1d8a8', 2.4);
    sun.position.set(-3,4,5);
    this.scene.add(sun);
    this.world.add(this.pins);
    this.scene.add(this.world);
    host.insertBefore(this.canvas, host.firstChild);
    void this.createEarth(biomeUrl,reliefUrl);
    this.resize(host.clientWidth,host.clientHeight);
    this.draw();
  }

  private async createEarth(biomeUrl:string,reliefUrl:string) {
    try {
      const [biomes,relief]=await Promise.all([loadImage(biomeUrl),loadImage(reliefUrl)]);
      if(this.disposed)return;
      const reliefCanvas=document.createElement('canvas');
      reliefCanvas.width=relief.width;reliefCanvas.height=relief.height;
      const reliefContext=reliefCanvas.getContext('2d',{willReadFrequently:true});
      if(!reliefContext)throw new Error('relief canvas unavailable');
      reliefContext.drawImage(relief,0,0);
      const pixels=reliefContext.getImageData(0,0,reliefCanvas.width,reliefCanvas.height).data;
      const geometry=new SphereGeometry(1,256,160);
      const positions=geometry.attributes.position;
      const uv=geometry.attributes.uv;
      const vector=new Vector3();
      for(let i=0;i<positions.count;i++){
        const u=uv.getX(i),latitude=(uv.getY(i)-.5)*Math.PI;
        const mercatorY=Math.max(0,Math.min(1,.5-Math.asinh(Math.tan(latitude))/(2*Math.PI)));
        const x=Math.min(reliefCanvas.width-1,Math.floor(u*reliefCanvas.width));
        const y=Math.min(reliefCanvas.height-1,Math.floor(mercatorY*reliefCanvas.height));
        const pixel=(y*reliefCanvas.width+x)*4;
        const height=(pixels[pixel]??0)/255;
        vector.fromBufferAttribute(positions,i).normalize().multiplyScalar(1+Math.max(0,height-.36)*.085);
        positions.setXYZ(i,vector.x,vector.y,vector.z);
      }
      geometry.computeVertexNormals();
      const map=new CanvasTexture(createCartography(biomes,relief));
      map.colorSpace=SRGBColorSpace;
      map.anisotropy=this.renderer.capabilities.getMaxAnisotropy();
      const material=new MeshStandardMaterial({map,roughness:0.94,metalness:0});
      this.earth=new Mesh(geometry,material);
      this.earth.rotation.y=-Math.PI/2;
      this.world.add(this.earth);
      this.draw();
    }catch(error){this.canvas.dataset.error=String(error);}
  }

  setCamera(center:Point,worldSize:number,width:number,height:number) {
    const lon=center.x*360-180;
    const lat=Math.atan(Math.sinh(Math.PI*(1-2*center.y)))*180/Math.PI;
    const latitude=lat*Math.PI/180,longitude=lon*Math.PI/180;
    this.center.set(Math.cos(latitude)*Math.sin(longitude),Math.sin(latitude),Math.cos(latitude)*Math.cos(longitude)).normalize();
    this.progress=Math.max(0,Math.min(1,Math.log(Math.max(worldSize,1200)/1200)/Math.log(8_388_608/1200)));
    const distance=5.2-4.05*this.progress;
    const rotation=new Vector3(0,0,1).normalize();
    this.world.quaternion.setFromUnitVectors(this.center,rotation);
    this.camera.position.set(0,0,distance);
    this.camera.lookAt(0,0,0);
    this.camera.fov=36;
    this.camera.aspect=Math.max(width,1)/Math.max(height,1);
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(width,height,false);
    this.canvas.dataset.lod=this.progress<.42?'illustrated-globe':this.progress<.82?'regional-relief':'terrain-close';
    this.draw();
  }

  setData(world:WorldSnapshot|null,admin:AdminSnapshot|null,movingRuler?:Coordinate) {
    const pins:Pin[]=[];
    for(const city of admin?.cities??world?.cities??[])if(city.pos)pins.push({key:`city:${city.id}`,pos:city.pos,kind:'city'});
    const ruler=world?.ruler;
    if(ruler?.pos)pins.push({key:`ruler:${ruler.id}`,pos:movingRuler??ruler.pos,kind:'ruler'});
    for(const entity of admin?.entities??[])if(entity.pos)pins.push({key:`entity:${entity.id}`,pos:entity.pos,kind:entity.kind===0?'ruler':'unit'});
    const key=pins.map(pin=>`${pin.key}:${pin.kind}`).join('|');
    if(key!==this.pinsKey){
      const nextKeys=new Set(pins.map(pin=>pin.key));
      for(const [oldKey,node] of this.pinNodes)if(!nextKeys.has(oldKey)){this.pins.remove(node);this.pinNodes.delete(oldKey);}
      for(const pin of pins)if(!this.pinNodes.has(pin.key)){const node=this.createMarker(pin.kind);this.pinNodes.set(pin.key,node);this.pins.add(node);}
      this.pinsKey=key;
    }
    for(const pin of pins){
      const lat=pin.pos.lat*Math.PI/180,lon=pin.pos.lon*Math.PI/180;
      const normal=new Vector3(Math.cos(lat)*Math.sin(lon),Math.sin(lat),Math.cos(lat)*Math.cos(lon));
      const marker=this.pinNodes.get(pin.key);if(!marker)continue;
      marker.position.copy(normal.clone().multiplyScalar(1.006));
      marker.quaternion.setFromUnitVectors(new Vector3(0,1,0),normal);
    }
    this.draw();
  }

  private createMarker(kind:Pin['kind']):Group{
    const marker=new Group();
    if(kind==='city'){
      for(const [x,z] of [[-.009,-.005],[.009,-.004],[0,.008]]){
        const house=new Mesh(this.cityBlock,this.cityWallMaterial);house.position.set(x,.008,z);marker.add(house);
        const roof=new Mesh(this.roof,this.cityRoofMaterial);roof.position.set(x,.02,z);marker.add(roof);
      }
    }else if(kind==='ruler'){
      const body=new Mesh(this.soldierBody,this.rulerMaterial);body.position.y=.015;marker.add(body);
      const head=new Mesh(this.soldierHead,this.cityWallMaterial);head.position.y=.028;marker.add(head);
      const flagstaff=new Mesh(this.pennant,this.ironMaterial);flagstaff.position.set(.012,.027,0);marker.add(flagstaff);
      const flag=new Mesh(this.cityBlock,this.pinMaterial);flag.scale.set(1.2,.7,.25);flag.position.set(.017,.032,0);marker.add(flag);
    }else{
      for(const [x,z] of [[-.008,-.006],[.008,-.006],[0,.008]]){
        const body=new Mesh(this.soldierBody,this.troopMaterial);body.position.set(x,.012,z);marker.add(body);
        const head=new Mesh(this.soldierHead,this.ironMaterial);head.position.set(x,.023,z);marker.add(head);
      }
    }
    return marker;
  }

  get detailBlend(){return this.progress;}

  coordinateAt(x:number,y:number):Coordinate|null{
    if(!this.earth)return null;
    const rect=this.canvas.getBoundingClientRect();
    const ndc=new Vector2((x-rect.left)/rect.width*2-1,-((y-rect.top)/rect.height)*2+1);
    this.raycaster.setFromCamera(ndc,this.camera);
    const hit=this.raycaster.intersectObject(this.earth,false)[0];
    if(!hit)return null;
    const local=this.earth.worldToLocal(hit.point.clone()).applyQuaternion(this.earth.quaternion).normalize();
    return {lat:Math.asin(local.y)*180/Math.PI,lon:Math.atan2(local.x,local.z)*180/Math.PI};
  }

  screenPosition(coordinate:Coordinate):Point|null{
    const lat=coordinate.lat*Math.PI/180,lon=coordinate.lon*Math.PI/180;
    const normal=new Vector3(Math.cos(lat)*Math.sin(lon),Math.sin(lat),Math.cos(lat)*Math.cos(lon));
    const point=this.world.localToWorld(normal.clone().multiplyScalar(1.11));
    if(normal.applyQuaternion(this.world.quaternion).dot(this.camera.position.clone().sub(point))<=0)return null;
    point.project(this.camera);
    if(point.z>1)return null;
    const rect=this.canvas.getBoundingClientRect();
    return {x:(point.x+1)*rect.width/2,y:(1-point.y)*rect.height/2};
  }

  resize(width:number,height:number){this.renderer.setSize(width,height,false);this.camera.aspect=Math.max(width,1)/Math.max(height,1);this.camera.updateProjectionMatrix();this.draw();}
  private draw(){if(this.disposed)return;if(this.frame)cancelAnimationFrame(this.frame);this.frame=requestAnimationFrame(()=>{this.frame=0;if(!this.disposed)this.renderer.render(this.scene,this.camera);});}
  destroy(){this.disposed=true;if(this.frame)cancelAnimationFrame(this.frame);this.renderer.dispose();
    for(const geometry of [this.pinGeometry,this.rulerGeometry,this.cityBlock,this.roof,this.soldierBody,this.soldierHead,this.pennant])geometry.dispose();
    for(const material of [this.pinMaterial,this.rulerMaterial,this.cityRoofMaterial,this.cityWallMaterial,this.troopMaterial,this.ironMaterial])material.dispose();
    this.canvas.remove();}
}

function loadImage(url:string):Promise<HTMLImageElement>{return new Promise((resolve,reject)=>{const image=new Image();image.onload=()=>resolve(image);image.onerror=()=>reject(new Error(`Unable to load map texture ${url}`));image.src=url;});}

/** Reprojects the biome raster and authors a subdued, hand-hatched atlas texture. */
function createCartography(source:HTMLImageElement,relief:HTMLImageElement):HTMLCanvasElement{
  const canvas=document.createElement('canvas');canvas.width=2048;canvas.height=1024;
  const context=canvas.getContext('2d',{willReadFrequently:true});
  const input=document.createElement('canvas');input.width=source.width;input.height=source.height;
  const inputContext=input.getContext('2d',{willReadFrequently:true});
  if(!context||!inputContext)throw new Error('cartography canvas unavailable');
  inputContext.drawImage(source,0,0);
  const sourcePixels=inputContext.getImageData(0,0,input.width,input.height).data;
  const elevationCanvas=document.createElement('canvas');elevationCanvas.width=relief.width;elevationCanvas.height=relief.height;
  const elevationContext=elevationCanvas.getContext('2d',{willReadFrequently:true});
  if(!elevationContext)throw new Error('relief texture unavailable');
  elevationContext.drawImage(relief,0,0);
  const elevationPixels=elevationContext.getImageData(0,0,elevationCanvas.width,elevationCanvas.height).data;
  const output=context.createImageData(canvas.width,canvas.height);
  for(let y=0;y<canvas.height;y++){
    const latitude=(.5-y/canvas.height)*Math.PI;
    const mercatorY=Math.max(0,Math.min(.999999,.5-Math.asinh(Math.tan(latitude))/(2*Math.PI)));
    const sourceY=Math.floor(mercatorY*input.height);
    for(let x=0;x<canvas.width;x++){
      const from=(sourceY*input.width+Math.floor(x/canvas.width*input.width))*4;
      const to=(y*canvas.width+x)*4;
      const alpha=sourcePixels[from+3]??0;
      const grain=.72+Math.sin((x+y*.73)*.018)*.025;
      if(alpha<96){output.data[to]=31;output.data[to+1]=49;output.data[to+2]=51;output.data[to+3]=255;continue;}
      const reliefX=Math.floor(x/canvas.width*elevationCanvas.width),reliefY=Math.floor(mercatorY*elevationCanvas.height);
      const elevation=(reliefY*elevationCanvas.width+reliefX)*4;
      const landShade=.68,reliefShade=.32;
      output.data[to]=Math.round(((sourcePixels[from]??0)*landShade+(elevationPixels[elevation]??0)*reliefShade)*grain*.78+25);
      output.data[to+1]=Math.round(((sourcePixels[from+1]??0)*landShade+(elevationPixels[elevation+1]??0)*reliefShade)*grain*.73+19);
      output.data[to+2]=Math.round(((sourcePixels[from+2]??0)*landShade+(elevationPixels[elevation+2]??0)*reliefShade)*grain*.67+15);
      output.data[to+3]=255;
    }
  }
  context.putImageData(output,0,0);
  for(let y=16;y<canvas.height;y+=23)for(let x=12;x<canvas.width;x+=23){
    const latitude=(.5-y/canvas.height)*Math.PI;
    const sourceY=Math.max(0,Math.min(input.height-1,Math.floor((.5-Math.asinh(Math.tan(latitude))/(2*Math.PI))*input.height)));
    const sourceX=Math.floor(x/canvas.width*input.width);
    if((sourcePixels[(sourceY*input.width+sourceX)*4+3]??0)<96)continue;
    const seed=Math.sin(x*12.9898+y*78.233)*43758.5453;
    const length=3+Math.abs(seed%7);
    context.beginPath();context.moveTo(x,y);context.lineTo(x+length,y-length*.52);
    context.strokeStyle=`rgba(39,31,24,${.12+Math.abs(seed%12)/100})`;context.lineWidth=.75;context.stroke();
  }
  return canvas;
}
