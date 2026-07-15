import { afterEach, describe, expect, it } from "vitest";
import { EditorView } from "@codemirror/view";
import { EditorState } from "@codemirror/state";
import { highlightStatementInEditor } from "@/utils/editorHighlight";

let view: EditorView;
afterEach(() => view?.destroy());

describe("highlightStatementInEditor", () => {
  it("selects the given range in the editor", () => {
    view = new EditorView({
      state: EditorState.create({ doc: "SELECT 1;\nSELECT 2;" }),
      parent: document.body,
    });
    highlightStatementInEditor(view, 10, 18);
    expect(view.state.selection.main.from).toBe(10);
    expect(view.state.selection.main.to).toBe(18);
  });
});
