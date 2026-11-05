import { useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { Atlas } from './map/Atlas.js';
import { useAtlas } from './state.js';
import { messages } from './i18n.js';
import { connectWorld } from './network.js';
import { PlayPanel } from './PlayPanel.js';
import { gameMessages } from './game-i18n.js';
import { CityPanel } from './CityPanel.js';
import { AdminPanel } from './AdminPanel.js';
import './brutal.css';
import './globe.css';

function App() {
  const host = useRef<HTMLDivElement>(null);
  const state = useAtlas();
  const t = messages[state.locale];
  const g=gameMessages[state.locale];
  const [panelOpen, setPanelOpen] = useState(false);
  const cityView = Boolean(state.city&&state.zoom>=15);
  const ownCity=state.world?.cities.find(city=>city.id===state.world?.ruler?.cityId);
  useEffect(()=>connectWorld(state.session?.token??null),[state.session?.token]);
  useEffect(() => {
    if (!host.current) return;
    const atlas = new Atlas(host.current);
    void atlas.init().catch((error: unknown) => {
      console.error('Atlas initialization failed', error);
      host.current?.setAttribute('data-init-error', String(error));
      useAtlas.setState({error: true});
    });
    return () => { atlas.destroy(); };
  }, []);
  return <main className={cityView ? 'city-view' : 'world-view'}>
    <div className="map" ref={host} tabIndex={0} role="application" aria-label={t.map} aria-describedby="keyboard-help" />
    <header>
      <a className="brand" href="/" aria-label="Orbis Terrae"><span className="brand-mark">◆</span><span>ORBIS <b>TERRAE</b><small>{t.atlas}</small></span></a>
      <div className="era"><span className="sun">{cityView ? '▲' : '◆'}</span><span>{state.world?.ruler?g.local:t.eraLabel}<small>{state.world?.ruler?t.eraLabel:t.era}</small></span></div>
      <div className="header-right"><span className="status"><i />{state.connection === 'online' ? t.connected : t.offline}</span>
        <select aria-label="Language / Idioma" value={state.locale} onChange={e => state.setLocale(e.target.value === 'en' ? 'en' : 'pt-BR')}>
          <option value="pt-BR">PT-BR</option><option value="en">EN</option>
        </select>
      </div>
    </header>
    <button className="details-toggle" aria-expanded={panelOpen} onClick={() => setPanelOpen(!panelOpen)}>
      <span className="toggle-icon">{state.session?'✕':'◆'}</span><span><strong>{state.session?ownCity?.name??g.nomad:g.begin}</strong><small>{panelOpen ? t.closeDetails : state.session?g.governor:g.profile}</small></span><span>{panelOpen ? '−' : '+'}</span>
    </button>
    <aside hidden={!panelOpen}>
      <div className="eyebrow"><span className="line" />{t.explore}</div>
      <h1>{t.title.split('\n').map((line, i) => <span key={line} className={i ? 'gold' : ''}>{line}<br /></span>)}</h1>
      <p className="intro">{t.intro}</p>
      <PlayPanel />
      <p className="preview-note"><strong>{t.phase}</strong>{t.notice}</p>
    </aside>
    <div className="map-controls">
      <div className="compass" aria-hidden="true"><span>N</span><b>▲</b></div>
      <div className="zoom-controls"><button aria-label={t.zoomIn} onClick={() => state.command('zoomIn')}>+</button>
        <span>{state.zoom.toFixed(1)}×</span><button aria-label={t.zoomOut} onClick={() => state.command('zoomOut')}>−</button></div>
      <button aria-label={t.reset} onClick={() => state.command('reset')}>✕</button>
    </div>
    <div className="map-caption"><span className="terrain-dot" />{state.session?g.hint:t.terrain}</div>
    {!state.session&&<div className="layer-control"><button aria-pressed={state.showDiagnostics} onClick={state.toggleDiagnostics}>{t.testLayer}{state.showDiagnostics ? ' ✓' : ''}</button></div>}
    {state.session&&<button className="follow-button" onClick={()=>state.command('follow')} aria-label={g.follow}>◆</button>}
    <CityPanel />
    <AdminPanel />
    <footer><span><a href="https://ecoregions.appspot.com/" target="_blank" rel="noreferrer">{t.source}</a></span><span aria-live="polite">{state.error ? t.error : state.session?g.keyboard:state.loaded ? t.ready : t.loading}</span></footer>
    <span id="keyboard-help" className="sr-only">{state.session?g.keyboard:t.keyboard}</span>
    {state.error && <div role="alert" className="error">{t.error}<button onClick={() => location.reload()}>{t.retry}</button></div>}
  </main>;
}

const root = document.getElementById('root');
if (root) createRoot(root).render(<App />);
