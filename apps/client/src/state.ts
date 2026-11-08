import { create } from 'zustand';
import type { Coordinate } from './map/projection.js';
import type { AdminSnapshot, CaravanState, CitySnapshot, EntityUpsert, HubState, MarketSnapshot, SetViewport, WorldSnapshot } from '@orbis/protocol';
import { storedSession, type LocalSession } from './account.js';
import type { RenderStyle } from './map/WorldRenderer.js';

export type Locale = 'pt-BR' | 'en';
export type CityTool =
  | {kind:'road';value:'road'|'none'}
  | {kind:'zone';value:'residential'|'agricultural'|'industrial'|'civic'|'military'|'none'}
  | {kind:'building';value:string};
interface AtlasState {
  session:LocalSession|null;
  setSession:(session:LocalSession|null)=>void;
  world:WorldSnapshot|null;
  city:CitySnapshot|null;
  adminSnapshot:AdminSnapshot|null;
  adminEventCursor:bigint;
  adminFilters:{accountId:string;kingdomId:string;cityId:string;shard:string;eventKind:string};
  selectedCityId:string|null;
  cityRotation:0|1|2|3;
  cityTool:CityTool;
  /** Economy v0 (S1.3b): hub roster, selected hub detail and its live market. */
  hubs:HubState[];
  selectedHubId:string|null;
  hub:HubState|null;
  hubMarket:MarketSnapshot|null;
  caravans:CaravanState[];
  serverOffsetMs:number;
  gameMessage:string|null;
  commandPending:boolean;
  showDiagnostics: boolean;
  toggleDiagnostics: () => void;
  entities: EntityUpsert[];
  viewport: SetViewport | null;
  connection: 'offline' | 'connecting' | 'online';
  locale: Locale;
  renderStyle: RenderStyle;
  selected: Coordinate | null;
  zoom: number;
  loaded: number;
  error: boolean;
  action: {kind: 'zoomIn' | 'zoomOut' | 'reset' | 'city' | 'follow'; serial: number} | null;
  setLocale: (locale: Locale) => void;
  setRenderStyle: (style: RenderStyle) => void;
  select: (selected: Coordinate) => void;
  command: (kind: 'zoomIn' | 'zoomOut' | 'reset' | 'city' | 'follow') => void;
  selectCity:(id:string|null)=>void;
  rotateCity:(delta:-1|1)=>void;
  setCityTool:(tool:CityTool)=>void;
  selectHub:(id:string|null)=>void;
}
export const useAtlas = create<AtlasState>((set) => ({
  session:storedSession(),world:null,city:null,adminSnapshot:null,adminEventCursor:0n,
  adminFilters:{accountId:'',kingdomId:'',cityId:'',shard:'',eventKind:''},selectedCityId:null,cityRotation:0,
  cityTool:{kind:'road',value:'road'},serverOffsetMs:0,gameMessage:null,commandPending:false,
  hubs:[],selectedHubId:null,hub:null,hubMarket:null,caravans:[],
  setSession:(session)=>{
    try {if (session) localStorage.setItem('orbis-session',JSON.stringify(session));else localStorage.removeItem('orbis-session');} catch { /* Session still works until this page closes. */ }
    set({session,world:null,city:null,adminSnapshot:null,adminEventCursor:0n,selectedCityId:null,gameMessage:null,commandPending:false,
      hubs:[],selectedHubId:null,hub:null,hubMarket:null,caravans:[]});
  },
  showDiagnostics: false,
  toggleDiagnostics: () => set(s => ({showDiagnostics: !s.showDiagnostics})),
  entities: [], viewport: null, connection: 'offline',
  locale: 'pt-BR', renderStyle: readRenderStyle(), selected: null, zoom: 2, loaded: 0, error: false, action: null,
  setLocale: (locale) => { document.documentElement.lang = locale; set({locale}); },
  setRenderStyle: (renderStyle) => { try {localStorage.setItem('orbis-render-style',renderStyle);} catch { /* local preference is optional */ } set({renderStyle}); },
  select: (selected) => set({selected}),
  command: (kind) => set((s) => ({action: {kind, serial: (s.action?.serial ?? 0) + 1}})),
  selectCity:(selectedCityId)=>set({selectedCityId,city:null}),
  rotateCity:(delta)=>set(s=>({cityRotation:((s.cityRotation+delta+4)%4) as 0|1|2|3})),
  setCityTool:(cityTool)=>set({cityTool}),
  // Selecting (or clearing) a hub drops stale detail/market snapshots until fresh ones arrive.
  selectHub:(selectedHubId)=>set({selectedHubId,hub:null,hubMarket:null}),
}));

function readRenderStyle():RenderStyle { try {return localStorage.getItem('orbis-render-style')==='manuscript'?'manuscript':'relief';} catch {return 'relief';} }
