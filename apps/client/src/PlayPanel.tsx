import { useState } from 'react';
import { enterWorld } from './account.js';
import { gameMessages } from './game-i18n.js';
import { useAtlas } from './state.js';
import { sendGame } from './network.js';

export function PlayPanel() {
  const state=useAtlas(),t=gameMessages[state.locale];
  const [register,setRegister]=useState(true),[username,setUsername]=useState(''),[password,setPassword]=useState('');
  const [busy,setBusy]=useState(false),[authError,setAuthError]=useState<string|null>(null);
  const [name,setName]=useState(t.villageDefault);
  const ruler=state.world?.ruler,city=state.world?.cities.find(city=>city.id===ruler?.cityId);
  const blocked=state.commandPending||!!ruler?.journey||!!ruler?.foundingUntilMs;
  const now=Date.now()+state.serverOffsetMs;
  const due=ruler?.journey?ruler.journeyDueMs:ruler?.foundingUntilMs??0;
  const remaining=Math.ceil(Math.max(0,due-now)/1000);
  const selected=state.selected;
  return <section className="panel play-panel">
    {!state.session?<>
      <span className="step">{t.profile}</span><h2>{t.begin}</h2><p>{t.intro}</p>
      {selected&&<p className="chosen-location">{t.selected}: {selected.lat.toFixed(3)}°, {selected.lon.toFixed(3)}°</p>}
      {selected&&<button className="text-button" onClick={()=>useAtlas.setState({selected:null})}>{t.suggested}</button>}
      <form onSubmit={async e=>{
        e.preventDefault();setBusy(true);setAuthError(null);
        try {state.setSession(await enterWorld(username,password,register,state.selected));setPassword('');}
        catch(error) {setAuthError(error instanceof Error?error.message:'server_error');}
        finally {setBusy(false);}
      }}>
        <label>{t.username}<input autoComplete="username" minLength={3} maxLength={32} required value={username} onChange={e=>setUsername(e.target.value)} /></label>
        <label>{t.password}<input type="password" autoComplete={register?'new-password':'current-password'} minLength={8} maxLength={128} required value={password} onChange={e=>setPassword(e.target.value)} /></label>
        <button className="city-button" disabled={busy} type="submit">{busy?t.waiting:register?t.create:t.login}<span>↗</span></button>
      </form>
      {authError&&<p className="game-feedback" role="alert">{t.errors[authError]??t.errors.server_error}</p>}
      <button className="text-button" onClick={()=>{setRegister(!register);setAuthError(null);}}>{register?t.haveAccount:t.newAccount}</button>
    </>:!ruler?<p>{t.waiting}</p>:<>
      <span className="step">{t.governor} · {state.session.username}</span>
      <h2>{city?.name??t.nomad}</h2>
      <p className="ruler-status" aria-live="polite">{ruler.journey?t.walking:ruler.foundingUntilMs?t.founding:city?.active?t.villageReady:t.idle}{due?` · ${remaining?remaining+t.seconds:t.finishing}`:''}</p>
      <dl><div><dt>{t.supplies}</dt><dd>{ruler.supplies}</dd></div><div><dt>{city?t.population:t.cost}</dt><dd>{city?city.population:state.world?.villageCost}</dd></div></dl>
      <button className="primary" onClick={()=>state.command('follow')}>{t.follow}<span>⌖</span></button>
      {selected&&<p className="chosen-location">{t.selected}: {selected.lat.toFixed(3)}°, {selected.lon.toFixed(3)}°</p>}
      <button className="city-button" disabled={!selected||blocked} onClick={()=>{if(selected)sendGame({$case:'moveRuler',moveRuler:{destination:selected}});}}>{t.move}<span>↗</span></button>
      {!city?<form onSubmit={e=>{e.preventDefault();if(!blocked&&ruler.pos)sendGame({$case:'foundCity',foundCity:{name,pos:ruler.pos}});}}>
        <label>{t.villageName}<input minLength={2} maxLength={48} required value={name} onChange={e=>setName(e.target.value)} /></label>
        <button className="city-button" disabled={blocked} type="submit">{t.found}<span>⌂</span></button>
        <p className="action-cost">{t.cost}: {state.world?.villageCost} · {(state.world?.villageConstructionMs??0)/1000}{t.seconds}</p>
      </form>:<button className="city-button" onClick={()=>state.command('city')}>{t.visit}<span>⌂</span></button>}
      {state.gameMessage&&<p className="game-feedback" role="status">{t.errors[state.gameMessage]??t.errors.server_error}</p>}
      <button className="text-button" onClick={()=>{state.setSession(null);state.command('reset');}}>{t.logout}</button>
    </>}
  </section>;
}
