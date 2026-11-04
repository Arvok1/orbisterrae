import { create } from 'zustand';
import type { Coordinate } from './map/projection.js';

export type Locale = 'pt-BR' | 'en';
interface AtlasState {
  locale: Locale;
  selected: Coordinate | null;
  zoom: number;
  loaded: number;
  error: boolean;
  action: {kind: 'zoomIn' | 'zoomOut' | 'reset'; serial: number} | null;
  setLocale: (locale: Locale) => void;
  select: (selected: Coordinate) => void;
  command: (kind: 'zoomIn' | 'zoomOut' | 'reset') => void;
}
export const useAtlas = create<AtlasState>((set) => ({
  locale: 'pt-BR', selected: null, zoom: 2, loaded: 0, error: false, action: null,
  setLocale: (locale) => { document.documentElement.lang = locale; set({locale}); },
  select: (selected) => set({selected}),
  command: (kind) => set((s) => ({action: {kind, serial: (s.action?.serial ?? 0) + 1}})),
}));
