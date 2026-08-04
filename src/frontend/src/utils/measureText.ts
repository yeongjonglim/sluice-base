// Measures rendered text width for the column-width estimator. A single canvas is
// reused across calls; getContext() is invoked each call (it returns the same
// cached context) so tests can stub it. When no 2D context exists — jsdom without
// the `canvas` package — we fall back to a flat per-character estimate.
const FALLBACK_CHAR_PX = 6.6;

let canvas: HTMLCanvasElement | null = null;

function context(): CanvasRenderingContext2D | null {
  try {
    canvas ??= document.createElement("canvas");
    return canvas.getContext("2d");
  } catch {
    return null;
  }
}

export function measureText(text: string, font: string): number {
  const ctx = context();
  if (!ctx) return text.length * FALLBACK_CHAR_PX;
  ctx.font = font;
  return ctx.measureText(text).width;
}
