import type { EditorView } from "@codemirror/view";

export function highlightStatementInEditor(
  view: EditorView,
  from: number,
  to: number,
): void {
  view.dispatch({
    selection: { anchor: from, head: to },
    scrollIntoView: true,
  });
  view.focus();
}
