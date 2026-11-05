import {
  BoxGeometry, Color, ConeGeometry, DataTexture, DirectionalLight, Group, HemisphereLight,
  InstancedMesh, Matrix4, Mesh, MeshBasicMaterial, MeshStandardMaterial, PerspectiveCamera,
  PlaneGeometry, Raycaster, Scene, ShaderMaterial, SphereGeometry, SRGBColorSpace, Texture,
  TextureLoader, Vector2, Vector3, WebGLRenderer, type Material,
} from 'three';
import type { AdminSnapshot, BuildingDefinition, CitySnapshot, WorldSnapshot } from '@orbis/protocol';
import biomeUrl from '../assets/world-biomes-v1.png?url';
import reliefUrl from '../assets/world-relief-v2.png?url';
import { citySurfacePixels, decodeRle } from './cityData.js';
import { latLonToUnit } from './geodesy.js';
import type { Coordinate } from './projection.js';
import { lodForZoom, type Lod } from './lod.js';

export type RenderStyle='relief'|'manuscript';
type Pin={id:string;kind:'city'|'ruler'|'unit';pos:Coordinate};
type Input={center:Coordinate;zoom:number;world:WorldSnapshot|null;admin:AdminSnapshot|null;city:CitySnapshot|null;cityPosition:Coordinate|null;cityRotation:number;style:RenderStyle;rulerPosition:Coordinate|null};

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
  private readonly pins=new Group();
  private readonly cityGroup=new Group();
  private readonly earthGeometry=new SphereGeometry(1,256,160);
  private earth:Mesh<import('three').BufferGeometry,ShaderMaterial>|null=null;
  private cityGround:Mesh<import('three').BufferGeometry,MeshBasicMaterial>|null=null;
  private citySurface:DataTexture|null=null;
  private buildings=new Group();
  private pinNodes=new Map<string,Group>();
  private cityKey='';
  private pinKey='';
  private currentCity:CitySnapshot|null=null;
  private style:RenderStyle='relief';
  private mode:Lod='global';
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
    this.world.add(this.pins);this.scene.add(this.world);
    this.cityGroup.add(this.buildings);this.cityGroup.visible=false;this.scene.add(this.cityGroup);
    host.appendChild(this.canvas);
    this.resize(host.clientWidth,host.clientHeight);
    this.renderer.setAnimationLoop(()=>this.render());
    void this.createEarth();
  }

  private async createEarth() {
    try {
      const loader=new TextureLoader();
      const [biomes,relief]=await Promise.all([loader.loadAsync(biomeUrl),loader.loadAsync(reliefUrl)]);
      if(this.disposed){biomes.dispose();relief.dispose();return;}
      biomes.colorSpace=SRGBColorSpace;biomes.anisotropy=this.renderer.capabilities.getMaxAnisotropy();
      relief.anisotropy=this.renderer.capabilities.getMaxAnisotropy();
      const material=new ShaderMaterial({
        uniforms:{biomes:{value:biomes},relief:{value:relief},style:{value:0},lightDir:{value:new Vector3(-.5,.7,.6).normalize()}},
        vertexShader:`varying vec2 vUv; varying vec3 vNormal; varying vec3 vPosition; uniform sampler2D relief;
          float mercator(float lat){return clamp(.5-asinh(tan(lat))/(2.0*3.14159265),0.0,1.0);}
          void main(){vUv=uv;float lat=(uv.y-.5)*3.14159265;vec2 m=vec2(uv.x,mercator(lat));float h=texture2D(relief,m).r;vec3 p=position*(1.0+max(0.0,h-.36)*.085);vNormal=normalize(normalMatrix*normal);vPosition=(modelViewMatrix*vec4(p,1.0)).xyz;gl_Position=projectionMatrix*vec4(vPosition,1.0);}`,
        fragmentShader:`uniform sampler2D biomes;uniform sampler2D relief;uniform float style;uniform vec3 lightDir;varying vec2 vUv;varying vec3 vNormal;
          float mercator(float lat){return clamp(.5-asinh(tan(lat))/(2.0*3.14159265),0.0,1.0);}
          void main(){float lat=(vUv.y-.5)*3.14159265;vec2 m=vec2(vUv.x,mercator(lat));vec4 base=texture2D(biomes,m);float h=texture2D(relief,m).r;float shade=.54+.46*max(0.0,dot(normalize(vNormal),normalize(lightDir)));vec3 reliefColor=mix(base.rgb*.58+vec3(.035,.025,.015),base.rgb*shade,0.76);
            float lines=(step(.955,fract((vUv.x+vUv.y*.38)*95.0))+step(.97,fract((vUv.x-vUv.y*.51)*73.0)))*.16;vec3 ink=vec3(.12,.085,.055);vec3 paper=mix(vec3(.14,.19,.18),vec3(.53,.42,.27),smoothstep(.12,.48,base.a));paper=mix(paper,ink,lines*smoothstep(.38,.76,h));vec3 color=mix(reliefColor,paper,.86*style);gl_FragColor=vec4(color,1.0);}`,
      });
      this.earth=new Mesh(this.earthGeometry,material);this.earth.rotation.y=-Math.PI/2;this.world.add(this.earth);
      this.canvas.dataset.ready='true';
    } catch(error) {this.canvas.dataset.error=String(error);}
  }

  update(input:Input) {
    const requestedLod=lodForZoom(input.zoom);
    // Deep zoom remains a regional globe until an authoritative city snapshot exists.
    // This prevents an empty canvas while an InspectCity response is in flight.
    this.mode=requestedLod==='city'&&input.city&&input.cityPosition?'city':requestedLod==='city'?'regional':requestedLod;this.style=input.style;
    this.updateGlobeCamera(input.center,input.zoom);
    this.syncPins(input.world,input.admin,input.rulerPosition);
    this.syncCity(input.city,input.cityPosition,input.cityRotation);
    this.world.visible=this.mode!=='city';this.cityGroup.visible=this.mode==='city'&&Boolean(this.cityGround);
    if(this.earth)this.earth.material.uniforms.style.value=this.style==='manuscript'?1:0;
    this.canvas.dataset.lod=this.mode;this.canvas.dataset.style=this.style;
  }

  private updateGlobeCamera(center:Coordinate,zoom:number) {
    if(this.mode==='city') {this.camera.fov=52;this.camera.position.set(0,2_250,1_900);this.camera.lookAt(0,0,0);this.camera.updateProjectionMatrix();return;}
    const normal=latLonToUnit(center),target=new Vector3(0,0,1);
    this.world.quaternion.setFromUnitVectors(new Vector3(normal.x,normal.y,normal.z),target);
    const progress=Math.max(0,Math.min(1,zoom/15));
    this.camera.fov=this.mode==='regional'?28:36;this.camera.position.set(0,0,5.2-3.5*progress);this.camera.lookAt(0,0,0);this.camera.updateProjectionMatrix();
  }

  private syncPins(world:WorldSnapshot|null,admin:AdminSnapshot|null,rulerPosition:Coordinate|null) {
    const pins:Pin[]=[];
    for(const city of admin?.cities??world?.cities??[])if(city.pos)pins.push({id:`city:${city.id}`,kind:'city',pos:city.pos});
    if(world?.ruler?.pos)pins.push({id:`ruler:${world.ruler.id}`,kind:'ruler',pos:rulerPosition??world.ruler.pos});
    for(const entity of admin?.entities??[])if(entity.pos)pins.push({id:`unit:${entity.id}`,kind:entity.kind===0?'ruler':'unit',pos:entity.pos});
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
    const stone=new MeshStandardMaterial({color:kind==='city'?'#b49b73':kind==='ruler'?'#d9bd78':'#6f4b3e',roughness:.85});
    if(kind==='city') {const base=new Mesh(new BoxGeometry(.025,.018,.025),stone);base.position.y=.012;node.add(base);const roof=new Mesh(new ConeGeometry(.021,.025,4),new MeshStandardMaterial({color:'#38271f',roughness:1}));roof.position.y=.033;node.add(roof);}
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
    if(!this.earth||this.mode==='city')return null;
    const rect=this.canvas.getBoundingClientRect(),pointer=new Vector2((clientX-rect.left)/rect.width*2-1,-(clientY-rect.top)/rect.height*2+1);this.raycaster.setFromCamera(pointer,this.camera);const hit=this.raycaster.intersectObject(this.earth,false)[0];if(!hit)return null;
    const point=this.world.worldToLocal(hit.point.clone()).applyQuaternion(this.earth.quaternion.clone().invert()).normalize();return {lat:Math.asin(point.y)*180/Math.PI,lon:Math.atan2(point.x,point.z)*180/Math.PI};
  }

  screenPosition(position:Coordinate):{x:number;y:number}|null {
    if(this.mode==='city')return null;const unit=latLonToUnit(position),point=this.world.localToWorld(new Vector3(unit.x,unit.y,unit.z).multiplyScalar(1.04));point.project(this.camera);if(point.z>1)return null;const rect=this.canvas.getBoundingClientRect();return {x:(point.x+1)*rect.width/2,y:(1-point.y)*rect.height/2};
  }

  resize(width:number,height:number) {if(width===this.lastResize.width&&height===this.lastResize.height)return;this.lastResize={width,height};this.camera.aspect=Math.max(1,width)/Math.max(1,height);this.camera.updateProjectionMatrix();this.renderer.setSize(width,height,false);}
  private render() {if(this.disposed)return;this.frameCount++;const elapsed=performance.now()-this.frameStarted;if(elapsed>1_000){const fps=this.frameCount*1_000/elapsed;this.canvas.dataset.fps=fps.toFixed(1);if(fps<34&&this.dpr>1){this.dpr=Math.max(1,this.dpr-.25);this.renderer.setPixelRatio(this.dpr);}this.frameStarted=performance.now();this.frameCount=0;}this.renderer.render(this.scene,this.camera);}
  destroy() {this.disposed=true;this.renderer.setAnimationLoop(null);this.earthGeometry.dispose();this.cityGround?.geometry.dispose();this.cityGround?.material.dispose();this.citySurface?.dispose();this.disposeGroup(this.pins);this.disposeGroup(this.buildings);this.earth?.material.dispose();this.renderer.dispose();this.canvas.remove();}
  private disposeGroup(group:Group) {group.traverse(object=>{const mesh=object as Mesh;if(mesh.geometry)mesh.geometry.dispose();const material=mesh.material as Material|Material[]|undefined;if(Array.isArray(material))material.forEach(item=>item.dispose());else material?.dispose();});}
}
