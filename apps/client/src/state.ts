import { create } from 'zustand';
import type { Coordinate } from './map/projection.js';
import type { EntityUpsert, SetViewport } from '@orbis/protocol';

export type Locale = 'pt-BR' | 'en';
interface AtlasState {
  cityBlend: number;
  showDiagnostics: boolean;
  toggleDiagnostics: () => void;
  entities: EntityUpsert[];
  viewport: SetViewport | null;
  connection: 'offline' | 'connecting' | 'online';
  locale: Locale;
  selected: Coordinate | null;
  zoom: number;
  loaded: number;
  error: boolean;
  action: {kind: 'zoomIn' | 'zoomOut' | 'reset' | 'city'; serial: number} | null;
  setLocale: (locale: Locale) => void;
  select: (selected: Coordinate) => void;
  command: (kind: 'zoomIn' | 'zoomOut' | 'reset' | 'city') => void;
}
export const useAtlas = create<AtlasState>((set) => ({
  cityBlend: 0, showDiagnostics: false,
  toggleDiagnostics: () => set(s => ({showDiagnostics: !s.showDiagnostics})),
  entities: [], viewport: null, connection: 'offline',
  locale: 'pt-BR', selected: null, zoom: 2, loaded: 0, error: false, action: null,
  setLocale: (locale) => { document.documentElement.lang = locale; set({locale}); },
  select: (selected) => set({selected}),
  command: (kind) => set((s) => ({action: {kind, serial: (s.action?.serial ?? 0) + 1}})),
}));
