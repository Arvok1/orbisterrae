/** Presentation-only camera transition. Never decides whether a game city exists. */
export function sceneBlend(zoom: number, hasSelection: boolean): number {
  if (!hasSelection || !Number.isFinite(zoom)) return 0;
  const progress = Math.max(0, Math.min(1, (zoom - 4.1) / 1.1));
  return progress * progress * (3 - 2 * progress);
}
