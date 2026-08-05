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

// A hidden offscreen span, reused across calls, that measures text the way the DOM
// will actually render it. Canvas metrics and real text layout diverge by a small,
// browser-dependent amount (Safari renders slightly wider than its canvas reports),
// so sizing a column from measureText alone can clip the tail in one browser while
// looking fine in another. Measuring in the same engine that paints the cell removes
// that gap — the width is exactly what will render, so the column fits everywhere
// with no cross-browser fudge factor. `nowrap` mirrors the cell's white-space.
let ruler: HTMLSpanElement | null = null;

function rulerEl(): HTMLSpanElement | null {
  if (typeof document === "undefined") return null;
  if (!ruler) {
    ruler = document.createElement("span");
    ruler.setAttribute("aria-hidden", "true");
    ruler.style.cssText =
      "position:absolute;top:-9999px;left:-9999px;visibility:hidden;white-space:nowrap;pointer-events:none;";
    document.body.appendChild(ruler);
  }
  return ruler;
}

export function measureRenderedText(text: string, font: string): number {
  const el = rulerEl();
  if (!el) return text.length * FALLBACK_CHAR_PX;
  el.style.font = font;
  el.textContent = text;
  const width = el.getBoundingClientRect().width;
  // Don't leave the measured text in the DOM — it would confuse Testing Library
  // text queries and screen readers. Empty span between measurements.
  el.textContent = "";
  // No layout engine (jsdom in tests) reports 0; fall back to the char estimate.
  return width > 0 ? width : text.length * FALLBACK_CHAR_PX;
}
