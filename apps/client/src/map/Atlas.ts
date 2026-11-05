import type { Point } from './projection.js';
import { project, unproject } from './projection.js';
import { useAtlas } from '../state.js';
import { sendGame } from '../network.js';
import { rulerPosition } from './gameScene.js';
import { WorldRenderer } from './WorldRenderer.js';

/** Browser input and Three.js rendering adapter. Simulation remains on the server. */
export class Atlas {
  private center:Point=project({lat:14,lon:8});
  private size=1200;
  private renderer:WorldRenderer|null=null;
  private observer:ResizeObserver|null=null;
  private abort=new AbortController();
  private unsubscribe:(()=>void)|null=null;
  private pointers=new Map<number,Point>();
  private start:Point|null=null;
  private moved=false;
  private pinch=0;
  private following=false;
  private soakTimer:ReturnType<typeof setInterval>|null=null;
  private disposed=false;

  constructor(private readonly host:HTMLElement) {}

  async init() {
    this.renderer=new WorldRenderer(this.host);
    this.size=Math.max(this.host.clientWidth*.95,800);
    const canvas=this.renderer.canvas,options={signal:this.abort.signal};
    canvas.addEventListener('wheel',event=>{event.preventDefault();const box=canvas.getBoundingClientRect();this.zoom(Math.exp(-event.deltaY*.0015),{x:event.clientX-box.left,y:event.clientY-box.top});},{...options,passive:false});
    canvas.addEventListener('pointerdown',event=>{canvas.setPointerCapture(event.pointerId);this.pointers.set(event.pointerId,{x:event.clientX,y:event.clientY});this.start={x:event.clientX,y:event.clientY};this.moved=false;if(this.pointers.size===2){this.pinch=this.distance();this.moved=true;}},options);
    canvas.addEventListener('pointermove',event=>{
      const previous=this.pointers.get(event.pointerId);if(!previous)return;this.pointers.set(event.pointerId,{x:event.clientX,y:event.clientY});
      if(this.pointers.size===2){const distance=this.distance();if(this.pinch>0)this.zoom(distance/this.pinch);this.pinch=distance;this.moved=true;return;}
      if(!this.start||Math.hypot(event.clientX-this.start.x,event.clientY-this.start.y)<=5)return;
      this.moved=true;this.following=false;this.center.x-=(event.clientX-previous.x)/this.size;this.center.y-=(event.clientY-previous.y)/this.size;this.draw();
    },options);
    canvas.addEventListener('pointerup',event=>{this.selectAt(event);this.pointers.delete(event.pointerId);this.start=null;},options);
    canvas.addEventListener('pointercancel',event=>{this.pointers.delete(event.pointerId);this.start=null;},options);
    canvas.addEventListener('keydown',event=>this.handleKey(event),options);
    this.unsubscribe=useAtlas.subscribe((state,before)=>{
      if(state.action!==before.action&&state.action)this.applyAction(state.action.kind);
      if(state.selected!==before.selected||state.action!==before.action||state.world!==before.world||state.city!==before.city||state.adminSnapshot!==before.adminSnapshot||state.cityRotation!==before.cityRotation||state.renderStyle!==before.renderStyle)this.draw();
    });
    this.observer=new ResizeObserver(()=>{this.renderer?.resize(this.host.clientWidth,this.host.clientHeight);this.draw();});this.observer.observe(this.host);
    this.draw();if(new URLSearchParams(location.search).has('soak'))this.startSoak();
  }

  private selectAt(event:PointerEvent) {
    if(this.moved||this.pointers.size!==1||!this.renderer)return;
    const state=useAtlas.getState(),cell=this.renderer.cityCellAt(event.clientX,event.clientY);
    if(cell&&state.city?.editable&&!state.commandPending){const tool=state.cityTool;
      if(tool.kind==='road')sendGame({$case:'applyCityEdits',applyCityEdits:{cityId:state.city.cityId,expectedRevision:state.city.revision,edits:[{x:cell.x,y:cell.y,layer:'road',value:'road'}]}});
      else if(tool.kind==='zone')sendGame({$case:'applyCityEdits',applyCityEdits:{cityId:state.city.cityId,expectedRevision:state.city.revision,edits:[{x:cell.x,y:cell.y,layer:'zone',value:tool.value}]}});
      else sendGame({$case:'queueBuilding',queueBuilding:{cityId:state.city.cityId,expectedRevision:state.city.revision,buildingId:tool.value,x:cell.x,y:cell.y,orientation:state.cityRotation}});
      return;
    }
    const rect=this.renderer.canvas.getBoundingClientRect(),screen={x:event.clientX-rect.left,y:event.clientY-rect.top},cities=state.adminSnapshot?.cities??state.world?.cities??[];
    const nearest=cities.map(city=>{const point=city.pos?this.renderer?.screenPosition(city.pos):null;return {city,distance:point?Math.hypot(point.x-screen.x,point.y-screen.y):Infinity};}).sort((a,b)=>a.distance-b.distance)[0];
    if(nearest&&nearest.distance<32){state.selectCity(nearest.city.id);sendGame({$case:'inspectCity',inspectCity:{cityId:nearest.city.id}});return;}
    const selected=this.renderer.coordinateAt(event.clientX,event.clientY);if(selected)state.select(selected);
  }

  private handleKey(event:KeyboardEvent) {
    const state=useAtlas.getState(),ruler=state.world?.ruler,direction:Record<string,Point>={w:{x:0,y:-1},s:{x:0,y:1},a:{x:-1,y:0},d:{x:1,y:0}},move=direction[event.key.toLowerCase()];
    if(move&&ruler&&!ruler.journey&&!ruler.foundingUntilMs&&!state.commandPending){event.preventDefault();const origin=project(rulerPosition(ruler,Date.now()+state.serverOffsetMs)),step=48/Math.max(this.size,8_388_608);sendGame({$case:'moveRuler',moveRuler:{destination:unproject({x:origin.x+move.x*step,y:origin.y+move.y*step})}});return;}
    if((event.key==='q'||event.key==='e')&&state.city&&state.zoom>=15){event.preventDefault();state.rotateCity(event.key==='q'?-1:1);return;}
    const step=80/this.size,actions:Record<string,()=>void>={ArrowLeft:()=>{this.center.x-=step;},ArrowRight:()=>{this.center.x+=step;},ArrowUp:()=>{this.center.y-=step;},ArrowDown:()=>{this.center.y+=step;},'+':()=>this.zoom(1.5),'=':()=>this.zoom(1.5),'-':()=>this.zoom(1/1.5),Enter:()=>useAtlas.getState().select(unproject(this.center))};
    if(actions[event.key]){event.preventDefault();actions[event.key]();this.draw();}
  }

  private applyAction(kind:'zoomIn'|'zoomOut'|'reset'|'city'|'follow') {
    const state=useAtlas.getState();
    if(kind==='reset'){this.following=false;state.selectCity(null);this.center=project({lat:14,lon:8});this.size=Math.max(this.host.clientWidth*.95,800);}
    else if(kind==='follow'&&state.world?.ruler){this.following=true;this.center=project(rulerPosition(state.world.ruler,Date.now()+state.serverOffsetMs));this.size=8_388_608;}
    else if(kind==='city'){const town=state.world?.cities.find(city=>city.id===state.world?.ruler?.cityId);if(town?.pos){this.following=false;this.center=project(town.pos);const cells=state.city?.width??256;this.size=Math.max(8_388_608,(this.host.clientWidth*.9)*40_075_017/(cells*10));state.selectCity(town.id);sendGame({$case:'inspectCity',inspectCity:{cityId:town.id}});}}
    else this.zoom(kind==='zoomIn'?1.5:1/1.5);
    this.draw();
  }

  private distance(){const[a,b]=[...this.pointers.values()];return a&&b?Math.hypot(a.x-b.x,a.y-b.y):0;}
  private worldAt(point:Point){return {x:this.center.x+(point.x-this.host.clientWidth/2)/this.size,y:this.center.y+(point.y-this.host.clientHeight/2)/this.size};}
  private zoom(factor:number,point={x:this.host.clientWidth/2,y:this.host.clientHeight/2}){const before=this.worldAt(point);this.size=Math.max(600,Math.min(1_073_741_824,this.size*factor));const after=this.worldAt(point);this.center.x+=before.x-after.x;this.center.y+=before.y-after.y;this.draw();}
  private draw(){if(this.disposed||!this.renderer)return;this.center.x=Math.max(0,Math.min(1,this.center.x));this.center.y=Math.max(0,Math.min(1,this.center.y));const state=useAtlas.getState(),now=Date.now()+state.serverOffsetMs;if(this.following&&state.world?.ruler)this.center=project(rulerPosition(state.world.ruler,now));const cityPosition=state.city?state.world?.cities.find(city=>city.id===state.city?.cityId)?.pos??null:null,zoom=Math.log2(this.size/256);
    this.renderer.update({center:unproject(this.center),zoom,world:state.world,admin:state.adminSnapshot,city:state.city,cityPosition,cityRotation:state.cityRotation,style:state.renderStyle,rulerPosition:state.world?.ruler?rulerPosition(state.world.ruler,now):null});
    useAtlas.setState({zoom,loaded:this.renderer.canvas.dataset.ready==='true'?1:0,viewport:{center:unproject(this.center),zoom,widthPx:this.host.clientWidth,heightPx:this.host.clientHeight}});
  }
  private startSoak(){const started=performance.now(),stops=[{lat:14,lon:8},{lat:46,lon:8},{lat:28,lon:84},{lat:-3,lon:-60}],report=document.createElement('output');report.className='soak-report';this.host.append(report);let step=0;this.soakTimer=setInterval(()=>{const seconds=Math.floor((performance.now()-started)/1000);this.center=project(stops[step%stops.length]);this.size=[1200,16384,1_048_576,16_777_216][step%4];step++;this.draw();report.textContent=JSON.stringify({seconds,lod:this.renderer?.canvas.dataset.lod,fps:this.renderer?.canvas.dataset.fps,dpr:this.renderer?.canvas.width&&this.host.clientWidth?this.renderer.canvas.width/this.host.clientWidth:1});if(seconds>=300&&this.soakTimer){clearInterval(this.soakTimer);this.soakTimer=null;report.dataset.result='completed';}},1000);}
  destroy(){this.disposed=true;this.abort.abort();this.observer?.disconnect();this.unsubscribe?.();if(this.soakTimer)clearInterval(this.soakTimer);this.renderer?.destroy();}
}
