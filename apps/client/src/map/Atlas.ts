import { Application, Assets, ColorMatrixFilter, Container, Graphics, Sprite, type Texture } from 'pixi.js';
import villageUrl from '../assets/village-concept-v1.png?url';
import terrainUrl from '../assets/world-terrain-v1.json?url';
import biomesUrl from '../assets/world-biomes-v1.png?url';
import land from '../assets/world-land-v1.json' with {type: 'json'};
import { project, unproject, visibleTiles, type Point } from './projection.js';
import { useAtlas } from '../state.js';
import { sceneBlend } from './scene.js';
import { TerrainLayer, type TerrainData } from './TerrainLayer.js';

/** Rendering only: no React dependency and no simulation results calculated here. */
export class Atlas {
  private app = new Application();
  private center: Point = project({lat: 14, lon: 8});
  private size = 1200;
  private disposed = false;
  private ready = false;
  private tiles = new Map<string, Sprite>();
  private pending = new Set<string>();
  private marker = new Graphics();
  private entities = new Graphics();
  private tilesLayer = new Container();
  private coastline = new Graphics();
  private terrain: TerrainLayer | null = null;
  private coastRings = land.coordinates.flatMap(polygon => polygon.map(ring =>
    ring.map(point => project({lon: point[0], lat: point[1]}))));
  private cityScene: Sprite | null = null;
  private cityTarget = 0;
  private observer: ResizeObserver | null = null;
  private abort = new AbortController();
  private unsubscribe: (() => void) | null = null;
  private pointers = new Map<number, Point>();
  private start: Point | null = null;
  private moved = false;
  private pinch = 0;
  private soakTimer: ReturnType<typeof setInterval> | null = null;

  constructor(private host: HTMLElement) {}

  async init() {
    await this.app.init({background: '#7eb8be', antialias: true,
      resolution: Math.min(devicePixelRatio, 2), autoDensity: true, resizeTo: this.host});
    if (this.disposed) { this.app.destroy(true); return; }
    this.ready = true;
    this.host.appendChild(this.app.canvas);
    const cartoon = new ColorMatrixFilter();
    cartoon.matrix = [
      0.6532258, 0, 0, 0, 0.4377609,
      0.1370968, 0, 0, 0, 0.7097407,
      -0.516129, 0, 0, 0, 0.7896268,
      0, 0, 0, 1, 0,
    ];
    this.tilesLayer.filters = [cartoon];
    this.app.stage.addChild(this.tilesLayer, this.coastline, this.entities, this.marker);
    const response = await fetch(terrainUrl, {signal: this.abort.signal});
    if (!response.ok) throw new Error(`Terrain load failed: ${response.status}`);
    const terrainData = await response.json() as TerrainData;
    if (this.disposed) return;
    const biomes = await Assets.load<Texture>(biomesUrl);
    if (this.disposed) { void Assets.unload(biomesUrl); return; }
    this.terrain = new TerrainLayer(this.app, terrainData, biomes);
    this.app.stage.addChildAt(this.terrain, 1);
    this.app.canvas.dataset.terrain = JSON.stringify(terrainData.stats);
    const village = await Assets.load<Texture>(villageUrl);
    if (this.disposed) { void Assets.unload(villageUrl); return; }
    this.cityScene = new Sprite(village);
    this.cityScene.anchor.set(0.5);
    this.cityScene.alpha = 0;
    this.app.stage.addChild(this.cityScene);
    let elapsed = 0;
    let frames = 0;
    this.app.ticker.add((ticker) => {
      if (this.cityScene) {
        const reduced = matchMedia('(prefers-reduced-motion: reduce)').matches;
        this.cityScene.alpha += (this.cityTarget - this.cityScene.alpha) * (reduced ? 1 : Math.min(1, ticker.deltaMS / 140));
        this.tilesLayer.alpha = this.marker.alpha = this.entities.alpha = this.coastline.alpha = 1 - this.cityScene.alpha;
        if (this.terrain) this.terrain.alpha = 1 - this.cityScene.alpha;
      }
      elapsed += ticker.deltaMS;
      frames++;
      if (elapsed >= 1000) {
        this.app.canvas.dataset.fps = (frames * 1000 / elapsed).toFixed(1);
        elapsed = 0;
        frames = 0;
      }
    });
    this.size = Math.max(this.host.clientWidth * 0.95, 800);
    const options = {signal: this.abort.signal};
    this.host.addEventListener('wheel', (e) => {
      e.preventDefault();
      const box = this.host.getBoundingClientRect();
      this.zoom(Math.exp(-e.deltaY * 0.0015), {x: e.clientX - box.left, y: e.clientY - box.top});
    }, {...options, passive: false});
    this.host.addEventListener('pointerdown', (e) => {
      this.host.setPointerCapture(e.pointerId);
      this.pointers.set(e.pointerId, {x: e.clientX, y: e.clientY});
      this.start = {x: e.clientX, y: e.clientY};
      this.moved = false;
      if (this.pointers.size === 2) { this.pinch = this.distance(); this.moved = true; }
    }, options);
    this.host.addEventListener('pointermove', (e) => {
      const previous = this.pointers.get(e.pointerId);
      if (!previous) return;
      this.pointers.set(e.pointerId, {x: e.clientX, y: e.clientY});
      if (this.pointers.size === 2) {
        const distance = this.distance();
        if (this.pinch > 0) this.zoom(distance / this.pinch);
        this.pinch = distance;
        this.moved = true;
      } else {
        if (this.start && Math.hypot(e.clientX - this.start.x, e.clientY - this.start.y) > 5) this.moved = true;
        this.center.x -= (e.clientX - previous.x) / this.size;
        this.center.y -= (e.clientY - previous.y) / this.size;
        this.draw();
      }
    }, options);
    this.host.addEventListener('pointerup', (e) => {
      if (!this.moved && this.pointers.size === 1) {
        const box = this.host.getBoundingClientRect();
        useAtlas.getState().select(unproject(this.worldAt({x: e.clientX - box.left, y: e.clientY - box.top})));
      }
      this.pointers.delete(e.pointerId);
    }, options);
    this.host.addEventListener('pointercancel', (e) => { this.pointers.delete(e.pointerId); }, options);
    this.host.addEventListener('keydown', (e) => {
      const step = 80 / this.size;
      const actions: Record<string, () => void> = {
        ArrowLeft: () => { this.center.x -= step; }, ArrowRight: () => { this.center.x += step; },
        ArrowUp: () => { this.center.y -= step; }, ArrowDown: () => { this.center.y += step; },
        '+': () => this.zoom(1.5), '=': () => this.zoom(1.5), '-': () => this.zoom(1 / 1.5),
        Enter: () => useAtlas.getState().select(unproject(this.center)),
      };
      if (actions[e.key]) { e.preventDefault(); actions[e.key](); this.draw(); }
    }, options);
    this.unsubscribe = useAtlas.subscribe((state, before) => {
      if (state.action !== before.action && state.action) {
        if (state.action.kind === 'reset') {
          this.center = project({lat: 14, lon: 8});
          this.size = Math.max(this.host.clientWidth * 0.95, 800);
        } else if (state.action.kind === 'city') {
          if (state.selected) { this.center = project(state.selected); this.size = 16384; }
        } else this.zoom(state.action.kind === 'zoomIn' ? 1.5 : 1 / 1.5);
      }
      if (state.selected !== before.selected || state.action !== before.action || state.entities !== before.entities || state.showDiagnostics !== before.showDiagnostics) this.draw();
    });
    this.observer = new ResizeObserver(() => {
      this.app.renderer.resize(this.host.clientWidth, this.host.clientHeight);
      this.draw();
    });
    this.observer.observe(this.host);
    this.draw();
    if (new URLSearchParams(location.search).has('soak')) this.startSoak();
  }

  /** Local QA mode: a repeatable five-minute camera tour; never emits game commands. */
  private startSoak() {
    const started = performance.now();
    let step = 0;
    let peakTiles = 0;
    let baselineHeap = 0;
    let minimumTailHeap = Infinity;
    const stops = [
      {lat: 14, lon: 8}, {lat: -23, lon: -46}, {lat: 40, lon: 110}, {lat: -26, lon: 135},
    ];
    const report = document.createElement('output');
    report.className = 'soak-report';
    report.setAttribute('aria-label', 'Map stability test');
    this.host.appendChild(report);
    this.soakTimer = setInterval(() => {
      const seconds = Math.floor((performance.now() - started) / 1000);
      const heap = (performance as Performance & {memory?: {usedJSHeapSize: number}}).memory?.usedJSHeapSize ?? 0;
      if (seconds >= 30 && !baselineHeap) baselineHeap = heap;
      if (seconds >= 240 && heap) minimumTailHeap = Math.min(minimumTailHeap, heap);
      peakTiles = Math.max(peakTiles, this.tiles.size);
      const snapshot = {seconds, tiles: this.tiles.size, pending: this.pending.size,
        peakTiles, fps: this.app.canvas.dataset.fps, heap, baselineHeap,
        minimumTailHeap: Number.isFinite(minimumTailHeap) ? minimumTailHeap : null};
      report.textContent = JSON.stringify(snapshot);
      if (seconds >= 300) {
        if (this.soakTimer) clearInterval(this.soakTimer);
        this.soakTimer = null;
        const heapStable = !heap || minimumTailHeap <= baselineHeap + 16 * 1024 * 1024;
        report.dataset.result = peakTiles <= 50 && this.pending.size < 50 && heapStable ? 'passed' : 'failed';
        report.textContent = JSON.stringify({...snapshot, result: report.dataset.result});
        return;
      }
      this.center = project(stops[step % stops.length]);
      this.size = [1200, 2400, 4800, 2400][step % 4];
      step++;
      this.draw();
    }, 1000);
  }

  private distance() {
    const [a, b] = [...this.pointers.values()];
    return a && b ? Math.hypot(a.x - b.x, a.y - b.y) : 0;
  }
  private worldAt(point: Point) {
    return {x: this.center.x + (point.x - this.host.clientWidth / 2) / this.size,
      y: this.center.y + (point.y - this.host.clientHeight / 2) / this.size};
  }
  private zoom(factor: number, point = {x: this.host.clientWidth / 2, y: this.host.clientHeight / 2}) {
    const before = this.worldAt(point);
    this.size = Math.max(600, Math.min(16384, this.size * factor));
    const after = this.worldAt(point);
    this.center.x += before.x - after.x;
    this.center.y += before.y - after.y;
    this.draw();
  }
  private draw() {
    if (this.disposed || !this.ready) return;
    this.center.x = Math.max(0, Math.min(1, this.center.x));
    this.center.y = Math.max(0, Math.min(1, this.center.y));
    const width = this.host.clientWidth, height = this.host.clientHeight;
    const visible = visibleTiles(this.center, this.size, width, height);
    const desired = new Set(visible.map(t => `/${t.z}/${t.x}/${t.y}.png`));
    for (const [url, sprite] of this.tiles) {
      if (!desired.has(url)) {
        sprite.destroy();
        this.tiles.delete(url);
        void Assets.unload(url);
      }
    }
    for (const tile of visible) {
      const url = `/${tile.z}/${tile.x}/${tile.y}.png`;
      const sprite = this.tiles.get(url);
      const n = 2 ** tile.z;
      if (sprite) {
        sprite.position.set(width / 2 + (tile.x / n - this.center.x) * this.size,
          height / 2 + (tile.y / n - this.center.y) * this.size);
        sprite.width = sprite.height = this.size / n + 0.5;
      } else if (!this.pending.has(url)) {
        this.pending.add(url);
        void Assets.load<Texture>(url).then(texture => {
          this.pending.delete(url);
          if (this.disposed) { void Assets.unload(url); return; }
          const sprite = new Sprite(texture);
          this.tiles.set(url, sprite);
          this.tilesLayer.addChild(sprite);
          this.draw();
        }).catch((error: unknown) => {
          this.host.dataset.loadError = String(error);
          this.pending.delete(url);
          useAtlas.setState({error: true});
        });
      }
    }
    this.marker.clear();
    this.terrain?.updateView(this.center, this.size, width, height);
    this.coastline.clear();
    for (const ring of this.coastRings) {
      const points = ring.flatMap(p => [width / 2 + (p.x - this.center.x) * this.size, height / 2 + (p.y - this.center.y) * this.size]);
      this.coastline.poly(points).stroke({color: '#64754c', width: 1.5, alpha: 0.7});
    }
    this.entities.clear();
    const entities = useAtlas.getState().showDiagnostics ? useAtlas.getState().entities : [];
    for (const entity of entities) {
      if (!entity.pos) continue;
      const p = project(entity.pos);
      this.entities.circle(width / 2 + (p.x - this.center.x) * this.size,
        height / 2 + (p.y - this.center.y) * this.size, this.size < 1500 ? 1.5 : 3);
    }
    this.entities.fill({color: '#e1bd79', alpha: 0.8});
    const selected = useAtlas.getState().selected;
    const zoom = Math.log2(this.size / 256);
    const cityProgress = selected ? Math.max(0, Math.min(1, (zoom - 4.1) / 1.1)) : 0;
    this.cityTarget = sceneBlend(zoom, selected !== null);
    if (this.cityScene) {
      this.cityScene.position.set(width / 2, height / 2);
      this.cityScene.scale.set(Math.max(width / this.cityScene.texture.width, height / this.cityScene.texture.height) * (1 + cityProgress * 0.08));
    }
    if (selected) {
      const p = project(selected);
      const x = width / 2 + (p.x - this.center.x) * this.size;
      const y = height / 2 + (p.y - this.center.y) * this.size;
      this.marker.circle(x, y, 18).stroke({color: '#ebc575', width: 1, alpha: 0.65});
      this.marker.circle(x, y, 5).fill('#ebc575');
      this.marker.moveTo(x - 26, y).lineTo(x - 12, y).moveTo(x + 12, y).lineTo(x + 26, y)
        .moveTo(x, y - 26).lineTo(x, y - 12).moveTo(x, y + 12).lineTo(x, y + 26)
        .stroke({color: '#ebc575', width: 1});
    }
    useAtlas.setState({zoom, loaded: this.tiles.size, cityBlend: this.cityTarget,
      viewport: {center: unproject(this.center), zoom: Math.log2(this.size / 256), widthPx: width, heightPx: height}});
    this.app.canvas.dataset.tiles = String(this.tiles.size);
    this.app.canvas.dataset.pending = String(this.pending.size);
  }
  destroy() {
    this.disposed = true;
    this.abort.abort();
    if (this.soakTimer) clearInterval(this.soakTimer);
    this.observer?.disconnect();
    this.unsubscribe?.();
    if (this.ready) this.app.destroy(true, {children: true});
    for (const url of this.tiles.keys()) void Assets.unload(url);
    void Assets.unload(villageUrl);
    void Assets.unload(biomesUrl);
    this.tiles.clear();
  }
}
