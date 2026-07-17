import type { EditorView } from "@codemirror/view";

// Small gap kept above the statement's first line so it doesn't sit flush
// against the very top edge of the editor.
const TOP_MARGIN = 12;

// Selects [from, to] and smoothly scrolls so the statement's first line is
// aligned near the top of the viewport — showing the whole block from its top,
// rather than CodeMirror's default "nearest edge" jump. Only scrolls when the
// block isn't already top-aligned, so re-clicking the active tab stays put.
export function highlightStatementInEditor(
  view: EditorView,
  from: number,
  to: number,
): void {
  view.dispatch({ selection: { anchor: from, head: to } });
  view.focus();

  const scroller = view.scrollDOM;
  // `lineBlockAt(from).top` is measured from the top of the document, the same
  // origin as scrollTop, so it maps directly to a scroll offset. It works for
  // lines that aren't currently rendered too (via the height map).
  const blockTop = view.lineBlockAt(from).top;
  const maxScroll = scroller.scrollHeight - scroller.clientHeight;
  const target = Math.max(0, Math.min(blockTop - TOP_MARGIN, maxScroll));

  if (Math.abs(target - scroller.scrollTop) > 1) {
    scroller.scrollTo({ top: target, behavior: "smooth" });
  }
}
