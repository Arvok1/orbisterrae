import { useAtlas, type CityTool } from './state.js';
import { sendGame } from './network.js';
import { gameMessages } from './game-i18n.js';

const zones=['residential','agricultural','industrial','civic','military'] as const;
type ZoneValue=Extract<CityTool,{kind:'zone'}>['value'];

export function CityPanel() {
  const state=useAtlas(),city=state.city,t=gameMessages[state.locale];
  if(!city||state.zoom<15) return null;
  const population=city.population?city.population.farmers+city.population.artisans+city.population.soldiers+city.population.priests:0;
  const set=(tool:CityTool)=>state.setCityTool(tool);
  return <section className="city-panel" aria-label="Controles da cidade">
    <div className="city-panel-head"><span><strong>{city.name}</strong><small>{city.editable?t.city.authoritative:t.city.readOnly} · rev. {city.revision.toString()}</small></span>
      <span className="city-rotation"><button aria-label={t.city.rotateLeft} onClick={()=>state.rotateCity(-1)}>Q ↶</button><button aria-label={t.city.rotateRight} onClick={()=>state.rotateCity(1)}>↷ E</button></span></div>
    <dl><div><dt>{t.city.population}</dt><dd>{population}</dd></div><div><dt>{t.city.water}</dt><dd>{Math.round((city.metrics?.water??0)*100)}%</dd></div><div><dt>{t.city.density}</dt><dd>{Math.round((city.metrics?.density??0)*100)}%</dd></div><div><dt>{t.city.defense}</dt><dd>{(city.metrics?.defense??0).toFixed(1)}</dd></div></dl>
    {city.population&&<dl><div><dt>{t.city.farmers}</dt><dd>{city.population.farmers}</dd></div><div><dt>{t.city.artisans}</dt><dd>{city.population.artisans}</dd></div><div><dt>{t.city.soldiers}</dt><dd>{city.population.soldiers}</dd></div><div><dt>{t.city.priests}</dt><dd>{city.population.priests}</dd></div></dl>}
    {city.events.length>0&&<p className="city-events">{city.events.map(event=>t.city.events[event.kind]??event.kind).join(' · ')}</p>}
    {city.editable&&<><div className="city-tools">
      <button aria-pressed={state.cityTool.kind==='road'} onClick={()=>set({kind:'road',value:'road'})}>{t.city.road}</button>
      <select aria-label={t.city.zone} value={state.cityTool.kind==='zone'?state.cityTool.value:'none'} onChange={event=>set({kind:'zone',value:event.target.value as ZoneValue})}>
        <option value="none">{t.city.zone}</option>{zones.map(zone=><option key={zone} value={zone}>{t.zones[zone]}</option>)}</select>
      <select aria-label={t.city.build} value={state.cityTool.kind==='building'?state.cityTool.value:''} onChange={event=>{if(event.target.value)set({kind:'building',value:event.target.value});}}>
        <option value="">{t.city.build}</option>{city.definitions.map(definition=><option key={definition.id} value={definition.id}>{t.buildings[definition.id]??definition.id}</option>)}</select>
    </div><p>{t.city.click}</p></>}
    {!!city.construction.length&&<ol className="construction-list">{city.construction.filter(order=>order.status==='active'||order.status==='queued').map(order=><li key={order.id}><span>{t.buildings[order.buildingId]??order.buildingId} · {order.status==='active'?t.city.active:t.city.queued}</span>{city.editable&&<button onClick={()=>sendGame({$case:'cancelConstruction',cancelConstruction:{cityId:city.cityId,expectedRevision:city.revision,orderId:order.id}})}>{t.city.cancel}</button>}</li>)}</ol>}
  </section>;
}
