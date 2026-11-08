import { useEffect, useState } from 'react';
import { useAtlas } from './state.js';
import { sendGame, requestHub, requestMarket } from './network.js';
import { gameMessages } from './game-i18n.js';
import { latLonToLocalMeters } from './map/geodesy.js';

const MAX_QTY=1_000_000;

/**
 * Minimal trade HUD (S1.3b economy v0): pick a hub, read its stock/metal/fee,
 * dispatch a caravan from the ruler's city. Book matching stays server-side.
 */
export function TradePanel() {
  const state=useAtlas(),t=gameMessages[state.locale];
  const ruler=state.world?.ruler;
  const hub=state.hubs.find(item=>item.id===state.selectedHubId)??state.hub;
  const [commodity,setCommodity]=useState('');
  const [qty,setQty]=useState(1);
  const [metal,setMetal]=useState(0);
  // Hub detail is a read-only command: refresh whenever a different hub is opened.
  useEffect(()=>{
    if(!state.selectedHubId) return;
    requestHub(state.selectedHubId);
  },[state.selectedHubId]);
  const goods=[...new Set(state.hubs.flatMap(item=>Object.keys(item.stock)))].sort();
  const chosen=goods.includes(commodity)?commodity:goods[0]??'';
  const chosenMarket=hub&&chosen?state.hubMarket?.hubId===hub.id&&state.hubMarket.commodity===chosen?state.hubMarket.localPrice:null:null;
  const offset=hub&&hub.pos&&ruler&&ruler.pos?latLonToLocalMeters(ruler.pos,hub.pos):null;
  const distance=offset?Math.round(Math.hypot(offset.east,offset.north)):null;
  const dispatch=()=>{
    if(!ruler||!ruler.cityId||!hub||!chosen) return;
    sendGame({$case:'createCaravan',createCaravan:{cityId:ruler.cityId,expectedRevision:ruler.revision,hubId:hub.id,cargo:{[chosen]:Math.max(0,Math.min(MAX_QTY,Math.floor(qty)))},metal:BigInt(Math.max(0,Math.floor(metal)))}});
  };
  if(!state.session||state.session.role==='admin'||!ruler||state.world===null||state.hubs.length===0) return null;
  return <section className="trade-panel city-panel" aria-label={t.trade.title}>
    <div className="city-panel-head">
      <span><strong>{t.trade.title}</strong><small>{state.hubs.length}</small></span>
      {state.selectedHubId&&<button onClick={()=>state.selectHub(null)}>✕</button>}
    </div>
    {!hub?<p>{t.trade.select}</p>
      :<>
        <dl>
          <div><dt>{t.trade.hub}</dt><dd>{hub.name}</dd></div>
          <div><dt>{t.trade.fee}</dt><dd>{(hub.fee*100).toFixed(0)}%</dd></div>
          <div><dt>{t.trade.metal}</dt><dd>{hub.metal.toString()}</dd></div>
          {distance!==null&&<div><dt>{t.trade.distance}</dt><dd>{`${Math.round(distance/1000).toLocaleString(state.locale==='en'?'en-US':'pt-BR')} km`}</dd></div>}
        </dl>
        <div className="trade-form">
          <label>{t.trade.goods} <select value={chosen} onChange={event=>{setCommodity(event.target.value);requestMarket(hub.id,event.target.value);}}>
            {goods.map(item=><option key={item} value={item}>{item}</option>)}
          </select></label>
          <label>{t.trade.qty} <input type="number" min={0} max={MAX_QTY} value={qty} onChange={event=>setQty(Number(event.target.value)||0)} /></label>
          <label>{t.trade.carried} <input type="number" min={0} value={metal} onChange={event=>setMetal(Number(event.target.value)||0)} /></label>
          <button disabled={!chosen||qty<=0} onClick={dispatch}>{t.trade.dispatch}</button>
          {chosenMarket!==null&&<small>{chosen}: {chosenMarket.toFixed(2)}</small>}
        </div>
        <div className="trade-caravans">
          {state.caravans.slice(-4).map(caravan=><small key={caravan.id}>
            {t.trade.status[caravan.status]??caravan.status} · {Object.entries(caravan.cargo).map(([item,quantity])=>`${item} ${quantity}`).join(', ')||'—'}
          </small>)}
          {state.caravans.length===0&&<small>{t.trade.select}</small>}
        </div>
      </>}
  </section>;
}
