import { afterEach, describe, expect, it, vi } from "vitest";
import { EditorView } from "@codemirror/view";
import { EditorState } from "@codemirror/state";
import { highlightStatementInEditor } from "@/utils/editorHighlight";

let view: EditorView | undefined;
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

  it("smooth-scrolls to top-align the statement when scrolled away", () => {
    const doc = Array.from({ length: 60 }, (_, i) => `SELECT ${i};`).join("\n");
    view = new EditorView({
      state: EditorState.create({ doc }),
      parent: document.body,
    });
    view.scrollDOM.scrollTop = 5000; // pretend the viewport scrolled far down
    const scrollTo = vi.spyOn(view.scrollDOM, "scrollTo").mockImplementation(() => {});

    highlightStatementInEditor(view, 0, 8); // first statement, at document top

    expect(scrollTo).toHaveBeenCalledTimes(1);
    expect(scrollTo.mock.calls[0][0]).toMatchObject({ top: 0, behavior: "smooth" });
  });

  it("does not scroll when the statement is already top-aligned", () => {
    view = new EditorView({
      state: EditorState.create({ doc: "SELECT 1;\nSELECT 2;" }),
      parent: document.body,
    });
    const scrollTo = vi.spyOn(view.scrollDOM, "scrollTo").mockImplementation(() => {});
    highlightStatementInEditor(view, 0, 8); // already at the top (scrollTop 0)
    expect(scrollTo).not.toHaveBeenCalled();
  });
});
