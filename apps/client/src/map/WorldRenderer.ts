import {
  BoxGeometry, BufferGeometry, Color, ConeGeometry, DataTexture, DirectionalLight, Group, HemisphereLight,
  Float32BufferAttribute, IcosahedronGeometry, InstancedMesh, LineBasicMaterial, LineSegments, Matrix4, Mesh, MeshBasicMaterial, MeshStandardMaterial, PerspectiveCamera, Shape, ShapeGeometry,
  PlaneGeometry, Raycaster, Scene, ShaderMaterial, SphereGeometry, SRGBColorSpace, Texture,
  TextureLoader, Vector2, Vector3, WebGLRenderer, type Material,
} from 'three';
import type { AdminSnapshot, BuildingDefinition, CitySnapshot, HubState, WorldSnapshot } from '@orbis/protocol';
import biomeUrl from '../assets/world-biomes-v1.png?url';
import reliefUrl from '../assets/world-relief-v2.png?url';
import terrainUrl from '../assets/world-terrain-v1.json?url';
import { citySurfacePixels, decodeRle } from './cityData.js';
import { latLonToLocalMeters, latLonToUnit, localMetersToLatLon } from './geodesy.js';
import type { Coordinate } from './projection.js';
import { lodForZoom, type Lod } from './lod.js';
import { inLake, mercatorSample, regionalSpan } from './regionData.js';

export type RenderStyle='relief'|'manuscript';
type Pin={id:string;kind:'city'|'ruler'|'unit'|'hub';pos:Coordinate};
type Input={center:Coordinate;zoom:number;world:WorldSnapshot|null;admin:AdminSnapshot|null;city:CitySnapshot|null;cityPosition:Coordinate|null;cityRotation:number;style:RenderStyle;rulerPosition:Coordinate|null;hubs:HubState[]};
type River={rank:number;width:number;lines:[number,number][][]};
type Lake={polygons:[number,number][][]};
type TerrainData={rivers:River[];lakes:Lake[]};

/**
 * The sole world renderer. It consumes server snapshots and contains no simulation decisions.
 * Globe coordinates are unit vectors; the close city uses metres in a local tangent frame.
 */
export class WorldRenderer {
  readonly canvas:HTMLCanvasElement;
  private readonly renderer:WebGLRenderer;
  private readonly scene=new Scene();
  private readonly camera=new PerspectiveCamera(36,1,.01,20_000);
  private readonly raycaster=new Raycaster();
  private readonly world=new Group();
  private readonly region=new Group();
  private readonly pins=new Group();
  private readonly cityGroup=new Group();
  private readonly earthGeometry=new SphereGeometry(1,256,160);
  private earth:Mesh<import('three').BufferGeometry,ShaderMaterial>|null=null;
  private cityGround:Mesh<import('three').BufferGeometry,MeshBasicMaterial>|null=null;
  private regionGround:Mesh<import('three').BufferGeometry,ShaderMaterial>|null=null;
  private citySurface:DataTexture|null=null;
  private buildings=new Group();
  private pinNodes=new Map<string,Group>();
  private cityKey='';
  private pinKey='';
  private currentCity:CitySnapshot|null=null;
  private style:RenderStyle='relief';
  private mode:Lod='global';
  private regionCenter:Coordinate={lat:0,lon:0};
  private regionSpan=0;
  private regionKey='';
  private terrain:TerrainData|null=null;
  private biomeTexture:Texture|null=null;
  private reliefTexture:Texture|null=null;
  private biomePixels:{data:Uint8ClampedArray;width:number;height:number}|null=null;
  private reliefPixels:{data:Uint8ClampedArray;width:number;height:number}|null=null;
  private lastInput:Input|null=null;
  private disposed=false;
  private frameStarted=performance.now();
  private frameCount=0;
  private dpr=Math.min(devicePixelRatio,1.5);
  private lastResize={width:0,height:0};

  constructor(private readonly host:HTMLElement) {
    this.renderer=new WebGLRenderer({antialias:true,powerPreference:'high-performance'});
    this.renderer.setPixelRatio(this.dpr);
    this.renderer.setClearColor('#111819');
    this.renderer.outputColorSpace=SRGBColorSpace;
    this.canvas=this.renderer.domElement;
    this.canvas.className='world-canvas';
    this.canvas.tabIndex=0;
    this.canvas.setAttribute('aria-label','Mapa 3D navegável');
    this.scene.background=new Color('#111819');
    this.scene.add(new HemisphereLight('#8b9aa0','#211b15',1.7));
    const sun=new DirectionalLight('#f0d4a4',2.6);sun.position.set(-4,5,6);this.scene.add(sun);
    this.world.add(this.pins);this.scene.add(this.world);this.region.visible=false;this.scene.add(this.region);
    this.cityGroup.add(this.buildings);this.cityGroup.visible=false;this.scene.add(this.cityGroup);
    host.appendChild(this.canvas);
    this.resize(host.clientWidth,host.clientHeight);
    this.renderer.setAnimationLoop(()=>this.render());
    void this.createEarth();
  }

  private async createEarth() {
    try {
      const loader=new TextureLoader();
      const [biomes,relief,terrain]=await Promise.all([loader.loadAsync(biomeUrl),loader.loadAsync(reliefUrl),fetch(terrainUrl).then(async response=>{if(!response.ok)throw new Error(`terrain ${response.status}`);return response.json() as Promise<TerrainData>;})]);
      if(this.disposed){biomes.dispose();relief.dispose();return;}
      biomes.colorSpace=SRGBColorSpace;biomes.anisotropy=this.renderer.capabilities.getMaxAnisotropy();this.biomeTexture=biomes;this.reliefTexture=relief;this.terrain=terrain;this.biomePixels=readPixels(biomes.image as HTMLImageElement);
      relief.anisotropy=this.renderer.capabilities.getMaxAnisotropy();this.reliefPixels=readPixels(relief.image as HTMLImageElement);
      const material=new ShaderMaterial({
        uniforms:{biomes:{value:biomes},relief:{value:relief},style:{value:0},lightDir:{value:new Vector3(-.5,.7,.6).normalize()}},
        vertexShader:`varying vec2 vUv; varying vec3 vNormal; varying vec3 vPosition; uniform sampler2D relief;
          float mercator(float lat){return clamp(.5-log(tan(.78539816+lat*.5))/(2.0*3.14159265),0.0,1.0);}
          void main(){vUv=uv;float lat=(uv.y-.5)*3.14159265;vec2 m=vec2(uv.x,1.0-mercator(lat));float h=texture2D(relief,m).r;vec3 p=position*(1.0+max(0.0,h-.36)*.085);vNormal=normalize(normalMatrix*normal);vPosition=(modelViewMatrix*vec4(p,1.0)).xyz;gl_Position=projectionMatrix*vec4(vPosition,1.0);}`,
        fragmentShader:`uniform sampler2D biomes;uniform sampler2D relief;uniform float style;uniform vec3 lightDir;varying vec2 vUv;varying vec3 vNormal;
          float mercator(float lat){return clamp(.5-log(tan(.78539816+lat*.5))/(2.0*3.14159265),0.0,1.0);}
          void main(){float lat=(vUv.y-.5)*3.14159265;vec2 m=vec2(vUv.x,1.0-mercator(lat));vec4 base=texture2D(biomes,m);float h=texture2D(relief,m).r;float shade=.54+.46*max(0.0,dot(normalize(vNormal),normalize(lightDir)));vec3 reliefColor=mix(base.rgb*.58+vec3(.035,.025,.015),base.rgb*shade,0.76);
            float lines=(step(.955,fract((vUv.x+vUv.y*.38)*95.0))+step(.97,fract((vUv.x-vUv.y*.51)*73.0)))*.16;vec3 ink=vec3(.12,.085,.055);vec3 paper=mix(vec3(.14,.19,.18),vec3(.53,.42,.27),smoothstep(.12,.48,base.a));paper=mix(paper,ink,lines*smoothstep(.38,.76,h));vec3 color=mix(reliefColor,paper,.86*style);gl_FragColor=vec4(color,1.0);}`,
      });
      this.earth=new Mesh(this.earthGeometry,material);this.earth.rotation.y=-Math.PI/2;this.world.add(this.earth);
      this.canvas.dataset.ready='true';
      if(this.lastInput)this.update(this.lastInput);
    } catch(error) {this.canvas.dataset.error=String(error);}
  }

  update(input:Input) {
    this.lastInput=input;
    const requestedLod=lodForZoom(input.zoom);
    // Deep zoom remains a regional globe until an authoritative city snapshot exists.
    // This prevents an empty canvas while an InspectCity response is in flight.
    const nearCity=input.cityPosition&&Math.hypot(...Object.values(latLonToLocalMeters(input.cityPosition,input.center)))<Math.max(2_000,regionalSpan(input.zoom));
    this.mode=requestedLod==='city'&&input.city&&nearCity?'city':requestedLod==='city'?'regional':requestedLod;this.style=input.style;
    this.updateGlobeCamera(input.center,input.zoom);
    this.syncRegion(input.center,input.zoom);
    this.syncPins(input.world,input.admin,input.rulerPosition,input.hubs);
    this.syncCity(input.city,input.cityPosition,input.cityRotation);
    this.world.visible=this.mode==='global';this.region.visible=this.mode==='regional'&&Boolean(this.regionGround);this.cityGroup.visible=this.mode==='city'&&Boolean(this.cityGround);
    if(this.earth)this.earth.material.uniforms.style.value=this.style==='manuscript'?1:0;
    this.canvas.dataset.lod=this.mode;this.canvas.dataset.style=this.style;
  }

  private updateGlobeCamera(center:Coordinate,zoom:number) {
    if(this.mode==='city') {const distance=Math.max(28,3_200/2**Math.max(0,zoom-15));this.camera.fov=52;this.camera.position.set(0,distance*.72,distance*.66);this.camera.lookAt(0,0,0);this.camera.near=.1;this.camera.far=20_000;this.camera.updateProjectionMatrix();return;}
    if(this.mode==='regional') {const span=regionalSpan(zoom);this.camera.fov=48;this.camera.position.set(0,span*.68,span*.42);this.camera.lookAt(0,0,0);this.camera.near=Math.max(.5,span/20_000);this.camera.far=span*8;this.camera.updateProjectionMatrix();return;}
    const normal=latLonToUnit(center),target=new Vector3(0,0,1);
    this.world.quaternion.setFromUnitVectors(new Vector3(normal.x,normal.y,normal.z),target);
    const progress=Math.max(0,Math.min(1,zoom/15));
    this.camera.near=.01;this.camera.far=20_000;this.camera.fov=36;this.camera.position.set(0,0,5.2-3.5*progress);this.camera.lookAt(0,0,0);this.camera.updateProjectionMatrix();
  }

  private syncRegion(center:Coordinate,zoom:number) {
    if(this.mode!=='regional'||!this.biomeTexture||!this.reliefTexture||!this.terrain)return;
    const span=regionalSpan(zoom),bucket=Math.floor(zoom*2)/2,grid=span/4;
    const key=`${bucket}:${Math.round(center.lat*111_195/grid)}:${Math.round(center.lon*111_195*Math.cos(center.lat*Math.PI/180)/grid)}:${this.style}`;
    if(key===this.regionKey){const shift=latLonToLocalMeters(this.regionCenter,center);this.region.position.set(-shift.east,0,shift.north);return;}
    this.regionKey=key;this.regionCenter=center;this.regionSpan=span;
    this.disposeGroup(this.region);this.region.clear();this.region.position.set(0,0,0);
    const geometry=new PlaneGeometry(span*2,span*2,128,128);
    geometry.rotateX(-Math.PI/2);
    const material=new ShaderMaterial({uniforms:{biomes:{value:this.biomeTexture},relief:{value:this.reliefTexture},center:{value:new Vector2(center.lon,center.lat)},extent:{value:span},style:{value:this.style==='manuscript'?1:0}},
      vertexShader:`uniform sampler2D relief;uniform vec2 center;uniform float extent;varying vec2 vMap;varying float vHeight;varying vec3 vNormal;
        void main(){float lat=center.y-position.z/111195.0;float lon=center.x+position.x/(111195.0*max(.08,cos(radians(center.y))));float a=radians(clamp(lat,-85.0,85.0));vMap=vec2(fract((lon+180.0)/360.0),1.0-clamp(.5-log(tan(.78539816+a*.5))/6.2831853,0.0,1.0));float h=texture2D(relief,vMap).r;vHeight=max(0.0,h-.36)*2600.0;vec3 p=position;p.y+=vHeight;vNormal=normal;gl_Position=projectionMatrix*modelViewMatrix*vec4(p,1.0);}`,
      fragmentShader:`uniform sampler2D biomes;uniform float style;varying vec2 vMap;varying float vHeight;varying vec3 vNormal;
        float hash(vec2 p){return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453);}
        void main(){vec4 biome=texture2D(biomes,vMap);float grain=hash(floor(vMap*500000.0));float contour=1.0-smoothstep(.86,1.0,fract(vHeight/72.0));vec3 relief=biome.a<.2?vec3(.055,.12,.15):biome.rgb*(.52+grain*.18)+vec3(vHeight/12000.0);vec3 paper=biome.a<.2?vec3(.16,.30,.30):vec3(.57,.45,.30)+biome.rgb*.12;paper=mix(paper,vec3(.18,.13,.09),contour*.25+grain*.05);gl_FragColor=vec4(mix(relief,paper,style),1.0);}`,
      side:2});
    this.regionGround=new Mesh(geometry,material);this.region.add(this.regionGround);
    this.addHydrography(center,span);
    this.addForest(center,span);
    this.canvas.dataset.region=`${center.lat.toFixed(4)},${center.lon.toFixed(4)}:${Math.round(span)}m`;
  }

  private addHydrography(center:Coordinate,span:number) {
    if(!this.terrain)return;
    const bounds=span*1.25,lakes=this.terrain.lakes.filter(lake=>lake.polygons.some(ring=>inLake([center.lon,center.lat],[ring])||ring.some(([lon,lat])=>{const p=latLonToLocalMeters(center,{lat,lon});return Math.abs(p.east)<bounds&&Math.abs(p.north)<bounds;})));
    const water=new MeshBasicMaterial({color:this.style==='manuscript'?'#668b84':'#23566a',side:2,depthWrite:true});
    for(const lake of lakes)for(const ring of lake.polygons){if(ring.length<3)continue;const points=ring.map(([lon,lat])=>latLonToLocalMeters(center,{lat,lon}));const shape=new Shape();shape.moveTo(points[0]!.east,-points[0]!.north);for(const p of points.slice(1))shape.lineTo(p.east,-p.north);const geometry=new ShapeGeometry(shape);geometry.rotateX(-Math.PI/2);const mesh=new Mesh(geometry,water);mesh.position.y=this.heightAt(center)+5;this.region.add(mesh);}
    const riverMaterial=new LineBasicMaterial({color:this.style==='manuscript'?'#5e817b':'#3a91a4',depthTest:true});
    const positions:number[]=[],lakePolygons=lakes.flatMap(lake=>lake.polygons);let drawn=0;
    for(const river of this.terrain.rivers){if(drawn>900)break;for(const line of river.lines){const visible=line.some(([lon,lat])=>{const p=latLonToLocalMeters(center,{lat,lon});return Math.abs(p.east)<bounds&&Math.abs(p.north)<bounds;});if(!visible)continue;
      let previous:Vector3|null=null;
      for(const [lon,lat] of line){if(inLake([lon,lat],lakePolygons)){previous=null;continue;}const p=latLonToLocalMeters(center,{lat,lon}),next=new Vector3(p.east,this.heightAt({lat,lon})+8,-p.north);if(previous&&!inLake([(lon+localMetersToLatLon(center,{east:previous.x,north:-previous.z}).lon)/2,(lat+localMetersToLatLon(center,{east:previous.x,north:-previous.z}).lat)/2],lakePolygons)){positions.push(previous.x,previous.y,previous.z,next.x,next.y,next.z);drawn++;}previous=next;}}}
    if(positions.length){const geometry=new BufferGeometry();geometry.setAttribute('position',new Float32BufferAttribute(positions,3));this.region.add(new LineSegments(geometry,riverMaterial));}
    this.canvas.dataset.hydro=`${drawn} rivers, ${lakes.length} lakes`;
  }

  private addForest(center:Coordinate,span:number) {
    if(!this.biomePixels)return;
    const geometry=new IcosahedronGeometry(1,1),material=new MeshStandardMaterial({color:'#ffffff',roughness:1,flatShading:true});
    const mesh=new InstancedMesh(geometry,material,2500),matrix=new Matrix4();let count=0;
    for(let iy=0;iy<50;iy++)for(let ix=0;ix<50;ix++){
      const jitterX=(Math.sin(ix*127.1+iy*311.7)*43758.5453%1)*.45,jitterY=(Math.sin(ix*269.5+iy*183.3)*12741.271%1)*.45;
      const east=((ix+jitterX)/49-.5)*span*1.8,north=((iy+jitterY)/49-.5)*span*1.8,position=localMetersToLatLon(center,{east,north});
      const sample=this.biomeAt(position);if(!sample||sample.a<96||sample.g<sample.r*.78||sample.g<sample.b*.8)continue;
      const seed=Math.abs(Math.sin((ix+11)*127.1+(iy+37)*311.7)*43758.5453)%1;if(seed>.68)continue;
      const height=Math.max(9,Math.min(90,span/600))*(.6+seed*.9);
      matrix.makeScale(height*(.45+seed*.3),height*.65,height*(.43+seed*.25));matrix.setPosition(east,this.heightAt(position)+height*.52,-north);mesh.setMatrixAt(count,matrix);
      mesh.setColorAt(count,new Color(this.style==='manuscript'?'#405744':'#2c5335').multiplyScalar(.72+seed*.45));count++;
    }
    mesh.count=count;mesh.instanceMatrix.needsUpdate=true;if(mesh.instanceColor)mesh.instanceColor.needsUpdate=true;this.region.add(mesh);
    this.canvas.dataset.forest=String(count);
  }

  private biomeAt(position:Coordinate) {if(!this.biomePixels)return null;const {u,v}=mercatorSample(position),{data,width,height}=this.biomePixels,index=(Math.min(height-1,Math.floor(v*height))*width+Math.min(width-1,Math.floor(u*width)))*4;return {r:data[index]??0,g:data[index+1]??0,b:data[index+2]??0,a:data[index+3]??0};}
  private heightAt(position:Coordinate) {if(!this.reliefPixels)return 0;const {u,v}=mercatorSample(position),{data,width,height}=this.reliefPixels,index=(Math.min(height-1,Math.floor(v*height))*width+Math.min(width-1,Math.floor(u*width)))*4;return Math.max(0,(data[index]??0)/255-.36)*2600;}

  private syncPins(world:WorldSnapshot|null,admin:AdminSnapshot|null,rulerPosition:Coordinate|null,hubs:HubState[]) {
    const pins:Pin[]=[];
    for(const city of admin?.cities??world?.cities??[])if(city.pos)pins.push({id:`city:${city.id}`,kind:'city',pos:city.pos});
    if(world?.ruler?.pos)pins.push({id:`ruler:${world.ruler.id}`,kind:'ruler',pos:rulerPosition??world.ruler.pos});
    for(const entity of admin?.entities??[])if(entity.pos)pins.push({id:`unit:${entity.id}`,kind:entity.kind===0?'ruler':'unit',pos:entity.pos});
    for(const hub of hubs)if(hub.pos)pins.push({id:`hub:${hub.id}`,kind:'hub',pos:hub.pos});
    const nextKey=pins.map(pin=>`${pin.id}:${pin.kind}:${pin.pos.lat.toFixed(4)}:${pin.pos.lon.toFixed(4)}`).join('|');
    if(nextKey===this.pinKey)return;this.pinKey=nextKey;
    const wanted=new Set(pins.map(pin=>pin.id));
    for(const [id,node] of this.pinNodes)if(!wanted.has(id)){this.pins.remove(node);this.pinNodes.delete(id);}
    for(const pin of pins) {
      let node=this.pinNodes.get(pin.id);if(!node){node=this.makePin(pin.kind);this.pinNodes.set(pin.id,node);this.pins.add(node);}
      const unit=latLonToUnit(pin.pos),normal=new Vector3(unit.x,unit.y,unit.z);node.position.copy(normal.multiplyScalar(1.008));node.quaternion.setFromUnitVectors(new Vector3(0,1,0),normal.normalize());
    }
  }

  private makePin(kind:Pin['kind']) {
    const node=new Group();
    const stone=new MeshStandardMaterial({color:kind==='city'?'#b49b73':kind==='ruler'?'#d9bd78':kind==='hub'?'#c9a227':'#6f4b3e',roughness:.85});
    if(kind==='city') {const base=new Mesh(new BoxGeometry(.025,.018,.025),stone);base.position.y=.012;node.add(base);const roof=new Mesh(new ConeGeometry(.021,.025,4),new MeshStandardMaterial({color:'#38271f',roughness:1}));roof.position.y=.033;node.add(roof);}
    else if(kind==='hub') {const base=new Mesh(new BoxGeometry(.03,.014,.03),stone);base.position.y=.009;node.add(base);const obelisk=new Mesh(new ConeGeometry(.016,.05,4),stone);obelisk.position.y=.041;node.add(obelisk);const cap=new Mesh(new BoxGeometry(.004,.026,.004),new MeshStandardMaterial({color:'#6f4b3e',roughness:1}));cap.position.set(.012,.04,0);node.add(cap);}
    else {const body=new Mesh(new BoxGeometry(.012,.028,.012),stone);body.position.y=.022;node.add(body);const flag=new Mesh(new BoxGeometry(.002,.035,.002),new MeshStandardMaterial({color:'#a84b36'}));flag.position.set(.011,.035,0);node.add(flag);}
    return node;
  }

  private syncCity(city:CitySnapshot|null,position:Coordinate|null,rotation:number) {
    if(!city||!position){this.cityGround?.removeFromParent();this.cityGround=null;this.currentCity=null;return;}
    const key=`${city.cityId}:${city.revision}:${rotation}:${this.style}`;
    if(key===this.cityKey)return;this.cityKey=key;this.currentCity=city;
    this.cityGround?.removeFromParent();this.cityGround?.geometry.dispose();this.cityGround?.material.dispose();this.citySurface?.dispose();
    try {
      const count=city.width*city.height,roads=Uint8Array.from(decodeRle(city.roadsRle,count)),zones=Uint8Array.from(decodeRle(city.zonesRle,count));
      this.citySurface=new DataTexture(citySurfacePixels(roads,zones,city.width,city.height),city.width,city.height);this.citySurface.needsUpdate=true;
      const material=new MeshBasicMaterial({map:this.citySurface,color:this.style==='manuscript'?'#c9ae74':'#60714b'});
      const ground=new Mesh(new PlaneGeometry(city.width*10,city.height*10,1,1),material);ground.rotation.x=-Math.PI/2;ground.position.set(0,-.2,0);this.cityGround=ground;this.cityGroup.add(ground);
      this.buildings.clear();this.addBuildings(city,rotation);this.cityGroup.rotation.y=rotation*Math.PI/2;
      this.canvas.dataset.city=`${city.width}x${city.height}@10m:${position.lat.toFixed(5)},${position.lon.toFixed(5)}`;
    }catch {this.canvas.dataset.cityError='invalid_rle';}
  }

  private addBuildings(city:CitySnapshot,rotation:number) {
    const definitions=new Map(city.definitions.map(definition=>[definition.id,definition]));
    const groups=new Map<string,{instances:typeof city.buildings;definition:BuildingDefinition}>();
    for(const building of city.buildings) {const definition=definitions.get(building.buildingId);if(!definition)continue;const bucket=groups.get(definition.visualRole)??{instances:[],definition};bucket.instances.push(building);groups.set(definition.visualRole,bucket);}
    const matrix=new Matrix4();
    for(const [role,bucket] of groups){const geometry=role==='tower'?new ConeGeometry(6,24,6):new BoxGeometry(1,1,1);const material=new MeshStandardMaterial({color:role==='field'?'#50653a':role==='wall'?'#696157':'#8d7655',roughness:.9});const mesh=new InstancedMesh(geometry,material,bucket.instances.length);
      bucket.instances.forEach((building,index)=>{const definition=bucket.definition,turn=(building.orientation+rotation)%2,width=(turn?definition.height:definition.width)*10,height=(turn?definition.width:definition.height)*10,tall=role==='tower'?24:Math.max(8,Math.min(24,(width+height)/3));matrix.makeScale(width,tall,height);matrix.setPosition((building.x+width/20-city.width/2)*10,tall/2,(building.y+height/20-city.height/2)*10);mesh.setMatrixAt(index,matrix);});mesh.instanceMatrix.needsUpdate=true;this.buildings.add(mesh);}
  }

  cityCellAt(clientX:number,clientY:number):{x:number;y:number}|null {
    const city=this.currentCity;if(!city||this.mode!=='city'||!this.cityGround)return null;
    const rect=this.canvas.getBoundingClientRect(),pointer=new Vector2((clientX-rect.left)/rect.width*2-1,-(clientY-rect.top)/rect.height*2+1);this.raycaster.setFromCamera(pointer,this.camera);
    const hit=this.raycaster.intersectObject(this.cityGround,false)[0];if(!hit)return null;
    const local=this.cityGroup.worldToLocal(hit.point.clone());const x=Math.floor(local.x/10+city.width/2),y=Math.floor(local.z/10+city.height/2);
    return x>=0&&y>=0&&x<city.width&&y<city.height?{x,y}:null;
  }

  coordinateAt(clientX:number,clientY:number):Coordinate|null {
    if(this.mode==='regional'&&this.regionGround){const rect=this.canvas.getBoundingClientRect(),pointer=new Vector2((clientX-rect.left)/rect.width*2-1,-(clientY-rect.top)/rect.height*2+1);this.raycaster.setFromCamera(pointer,this.camera);const hit=this.raycaster.intersectObject(this.regionGround,false)[0];if(!hit)return null;return localMetersToLatLon(this.regionCenter,{east:hit.point.x-this.region.position.x,north:-(hit.point.z-this.region.position.z)});}
    if(!this.earth||this.mode==='city')return null;
    const rect=this.canvas.getBoundingClientRect(),pointer=new Vector2((clientX-rect.left)/rect.width*2-1,-(clientY-rect.top)/rect.height*2+1);this.raycaster.setFromCamera(pointer,this.camera);const hit=this.raycaster.intersectObject(this.earth,false)[0];if(!hit)return null;
    const point=this.world.worldToLocal(hit.point.clone()).applyQuaternion(this.earth.quaternion.clone().invert()).normalize();return {lat:Math.asin(point.y)*180/Math.PI,lon:Math.atan2(point.x,point.z)*180/Math.PI};
  }

  screenPosition(position:Coordinate):{x:number;y:number}|null {
    if(this.mode==='city')return null;const point=this.mode==='regional'?(()=>{const local=latLonToLocalMeters(this.regionCenter,position);return new Vector3(local.east+this.region.position.x,this.heightAt(position)+15,-local.north+this.region.position.z);})():(()=>{const unit=latLonToUnit(position);return this.world.localToWorld(new Vector3(unit.x,unit.y,unit.z).multiplyScalar(1.04));})();point.project(this.camera);if(point.z>1)return null;const rect=this.canvas.getBoundingClientRect();return {x:(point.x+1)*rect.width/2,y:(1-point.y)*rect.height/2};
  }

  resize(width:number,height:number) {if(width===this.lastResize.width&&height===this.lastResize.height)return;this.lastResize={width,height};this.camera.aspect=Math.max(1,width)/Math.max(1,height);this.camera.updateProjectionMatrix();this.renderer.setSize(width,height,false);}
  private render() {if(this.disposed)return;this.frameCount++;const elapsed=performance.now()-this.frameStarted;if(elapsed>1_000){const fps=this.frameCount*1_000/elapsed;this.canvas.dataset.fps=fps.toFixed(1);if(fps<34&&this.dpr>1){this.dpr=Math.max(1,this.dpr-.25);this.renderer.setPixelRatio(this.dpr);}this.frameStarted=performance.now();this.frameCount=0;}this.renderer.render(this.scene,this.camera);}
  destroy() {this.disposed=true;this.renderer.setAnimationLoop(null);this.earthGeometry.dispose();this.cityGround?.geometry.dispose();this.cityGround?.material.dispose();this.citySurface?.dispose();this.disposeGroup(this.pins);this.disposeGroup(this.buildings);this.disposeGroup(this.region);this.earth?.material.dispose();this.biomeTexture?.dispose();this.reliefTexture?.dispose();this.renderer.dispose();this.canvas.remove();}
  private disposeGroup(group:Group) {group.traverse(object=>{const mesh=object as Mesh;if(mesh.geometry)mesh.geometry.dispose();const material=mesh.material as Material|Material[]|undefined;if(Array.isArray(material))material.forEach(item=>item.dispose());else material?.dispose();});}
}

function readPixels(image:HTMLImageElement){const canvas=document.createElement('canvas');canvas.width=image.naturalWidth;canvas.height=image.naturalHeight;const context=canvas.getContext('2d',{willReadFrequently:true});if(!context)throw new Error('terrain pixels unavailable');context.drawImage(image,0,0);return {data:context.getImageData(0,0,canvas.width,canvas.height).data,width:canvas.width,height:canvas.height};}
