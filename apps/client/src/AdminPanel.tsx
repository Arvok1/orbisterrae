import { useAtlas } from './state.js';

const filters = [
  ['accountId', 'accountId'], ['kingdomId', 'kingdomId'], ['cityId', 'cityId'],
  ['shard', 'shard'], ['eventKind', 'eventKind'],
] as const;

export function AdminPanel() {
  const state = useAtlas();
  if (state.session?.role !== 'admin') return null;
  const english=state.locale==='en';
  const labels:Record<string,string>=english?{accountId:'Account',kingdomId:'Kingdom',cityId:'City',shard:'Shard',eventKind:'Event type'}:
    {accountId:'Conta',kingdomId:'Reino',cityId:'Cidade',shard:'Shard',eventKind:'Tipo de evento'};
  const update = (key: keyof typeof state.adminFilters, value: string) => {
    useAtlas.setState(s => ({adminFilters:{...s.adminFilters,[key]:value},adminEventCursor:0n}));
  };
  const snapshot = state.adminSnapshot;
  return <section className="admin-panel" aria-label="Observatório administrativo">
    <header><span><strong>{english?'World observatory':'Observatório do mundo'}</strong><small>{english?'Global view · no fog of war · read only':'Visão global · sem neblina de guerra · somente leitura'}</small></span>
      <span className="admin-live"><i /> {english?'LIVE':'AO VIVO'}</span></header>
    <div className="admin-summary"><span>{snapshot?.cities.length ?? '—'} {english?'cities':'cidades'}</span><span>{snapshot?.entities.length ?? '—'} {english?'entities':'entidades'}</span>
      <span>{snapshot?.territory.length ?? '—'} {english?'cells':'células'}</span></div>
    <div className="admin-filters">{filters.map(([key,label])=><label key={key}>{labels[label]}
      <input type={key==='shard'||key==='eventKind'?'number':'text'} min="0" value={state.adminFilters[key]} onChange={e=>update(key,e.target.value)} placeholder={english?'All':'Todos'} />
    </label>)}</div>
    <ol className="admin-events" aria-live="polite">{snapshot?.events.slice().reverse().map(event=><li key={event.seq.toString()}>
      <time dateTime={new Date(event.atMs).toISOString()}>{new Date(event.atMs).toLocaleTimeString(state.locale)}</time>
      <strong>{english?'Event':'Evento'} {event.kind}</strong><span>{event.region || `Shard ${event.shard}`}</span>
      <small>{[event.accountId,event.kingdomId,event.cityId].filter(Boolean).join(' · ') || event.payloadJson}</small>
    </li>)}</ol>
    {!snapshot?.events.length&&<p className="admin-empty">{english?'Waiting for world events…':'Aguardando eventos do mundo…'}</p>}
  </section>;
}
