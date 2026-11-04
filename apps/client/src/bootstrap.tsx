import { useEffect, useRef, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { Atlas } from './map/Atlas.js';
import { useAtlas } from './state.js';
import { messages } from './i18n.js';
import { connectWorld } from './network.js';
import './cartoon.css';

function App() {
  const host = useRef<HTMLDivElement>(null);
  const state = useAtlas();
  const t = messages[state.locale];
  const [panelOpen, setPanelOpen] = useState(false);
  const cityView = state.cityBlend > 0.5;
  useEffect(() => {
    const disconnect = connectWorld();
    if (!host.current) return;
    const atlas = new Atlas(host.current);
    void atlas.init().catch((error: unknown) => {
      console.error('Atlas initialization failed', error);
      host.current?.setAttribute('data-init-error', String(error));
      useAtlas.setState({error: true});
    });
    return () => { disconnect(); atlas.destroy(); };
  }, []);
  return <main className={cityView ? 'city-view' : 'world-view'}>
    <div className="map" ref={host} tabIndex={0} role="application" aria-label={t.map} aria-describedby="keyboard-help" />
    <header>
      <a className="brand" href="/" aria-label="Orbis Terrae"><span className="brand-mark">⊕</span><span>ORBIS <b>TERRAE</b><small>{t.atlas}</small></span></a>
      <div className="era"><span className="sun">{cityView ? '⌂' : '☼'}</span><span>{cityView ? t.cityScene : t.eraLabel}<small>{cityView ? t.artPreview : t.era}</small></span></div>
      <div className="header-right"><span className="status"><i />{state.connection === 'online' ? t.connected : t.offline}</span>
        <select aria-label="Language / Idioma" value={state.locale} onChange={e => state.setLocale(e.target.value === 'en' ? 'en' : 'pt-BR')}>
          <option value="pt-BR">PT-BR</option><option value="en">EN</option>
        </select>
      </div>
    </header>
    <button className="details-toggle" aria-expanded={panelOpen} onClick={() => setPanelOpen(!panelOpen)}>
      <span className="toggle-icon">{cityView ? '⌂' : '⊕'}</span><span><strong>{cityView ? t.cityScene : t.worldScene}</strong><small>{panelOpen ? t.closeDetails : t.openDetails}</small></span><span>{panelOpen ? '−' : '+'}</span>
    </button>
    <aside hidden={!panelOpen}>
      <div className="eyebrow"><span className="line" />{t.explore}</div>
      <h1>{t.title.split('\n').map((line, i) => <span key={line} className={i ? 'gold' : ''}>{line}<br /></span>)}</h1>
      <p className="intro">{t.intro}</p>
      <section className="panel">
        <span className="step">{t.step}</span><h2>{t.region}</h2><p>{t.detail}</p>
        {state.selected ? <div className="location"><span className="eyebrow">{t.selected}</span>
          <dl><div><dt>{t.latitude}</dt><dd>{state.selected.lat.toFixed(3)}°</dd></div>
            <div><dt>{t.longitude}</dt><dd>{state.selected.lon.toFixed(3)}°</dd></div></dl>
        </div> : <div className="empty"><span>⌖</span><h3>{t.empty}</h3><p>{t.emptyDetail}</p></div>}
        <button className="primary" onClick={() => state.command('reset')}>{t.clear}<span>↗</span></button>
        <button className="city-button" disabled={!state.selected} onClick={() => {state.command('city');setPanelOpen(false);}}>{t.visitPreview}<span>⌂</span></button>
      </section>
      <p className="preview-note"><strong>{t.phase}</strong>{t.notice}</p>
    </aside>
    <div className="map-controls">
      <div className="compass" aria-hidden="true"><span>N</span><b>✧</b></div>
      <div className="zoom-controls"><button aria-label={t.zoomIn} onClick={() => state.command('zoomIn')}>+</button>
        <span>{state.zoom.toFixed(1)}×</span><button aria-label={t.zoomOut} onClick={() => state.command('zoomOut')}>−</button></div>
      <button aria-label={t.reset} onClick={() => state.command('reset')}>⌾</button>
    </div>
    <div className="map-caption"><span className="terrain-dot" />{t.terrain}<span className="divider" />{t.controls}</div>
    <div className="layer-control"><button aria-pressed={state.showDiagnostics} onClick={state.toggleDiagnostics}>{t.testLayer}{state.showDiagnostics ? ' ✓' : ''}</button></div>
    <footer><span>{cityView ? t.artPreview : t.source}</span><span aria-live="polite">{state.error ? t.error : state.loaded ? cityView ? t.zoomBack : t.zoomVillage : t.loading}</span></footer>
    <span id="keyboard-help" className="sr-only">{t.keyboard}</span>
    {state.error && <div role="alert" className="error">{t.error}<button onClick={() => location.reload()}>{t.retry}</button></div>}
  </main>;
}

const root = document.getElementById('root');
if (root) createRoot(root).render(<App />);
