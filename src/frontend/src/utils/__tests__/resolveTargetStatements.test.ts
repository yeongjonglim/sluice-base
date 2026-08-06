import { describe, expect, it } from "vitest";
import type { QueryEditorViewLike } from "@/utils/resolveTargetStatements";
import { buildExplainDispatch, resolveTargetStatements } from "@/utils/resolveTargetStatements";

// "SELECT 1;\nSELECT 2;\nSELECT 3"
//  s0 pos 0..8   s1 pos 10..18   s2 pos 20..28
const SQL = "SELECT 1;\nSELECT 2;\nSELECT 3";

function fakeView(doc: string, from: number, to: number, empty?: boolean): QueryEditorViewLike {
  return {
    state: {
      doc: { toString: () => doc },
      selection: { main: { from, to, empty: empty ?? from === to } },
    },
  };
}

describe("resolveTargetStatements", () => {
  it("returns [] when the view is null or undefined", () => {
    expect(resolveTargetStatements(null, false)).toEqual([]);
    expect(resolveTargetStatements(undefined, false)).toEqual([]);
  });

  it("returns [] for an empty document", () => {
    expect(resolveTargetStatements(fakeView("   ", 0, 0), false)).toEqual([]);
  });

  it("returns the statement under the cursor when the selection is empty and runAll is false", () => {
    const r = resolveTargetStatements(fakeView(SQL, 12, 12), false);
    expect(r.map((s) => s.text)).toEqual(["SELECT 2"]);
  });

  it("returns every statement a multi-statement selection touches", () => {
    const r = resolveTargetStatements(fakeView(SQL, 3, 24, false), false);
    expect(r.map((s) => s.text)).toEqual(["SELECT 1", "SELECT 2", "SELECT 3"]);
  });

  it("returns all statements when runAll is true, regardless of cursor position", () => {
    const r = resolveTargetStatements(fakeView(SQL, 12, 12), true);
    expect(r.map((s) => s.text)).toEqual(["SELECT 1", "SELECT 2", "SELECT 3"]);
  });
});

describe("buildExplainDispatch", () => {
  it("returns null when no database is selected", () => {
    expect(buildExplainDispatch(null, fakeView(SQL, 12, 12), false)).toBeNull();
  });

  it("returns null when the view has no statements", () => {
    expect(buildExplainDispatch("db-1", fakeView("   ", 0, 0), false)).toBeNull();
  });

  it("returns null when the view is missing", () => {
    expect(buildExplainDispatch("db-1", null, false)).toBeNull();
  });

  it("scopes to the statement under the cursor, ignoring runAll semantics (Explain never runs 'all')", () => {
    const dispatch = buildExplainDispatch("db-1", fakeView(SQL, 12, 12), false);
    expect(dispatch?.targets.map((s) => s.text)).toEqual(["SELECT 2"]);
  });

  it("carries the databaseId through unchanged", () => {
    const dispatch = buildExplainDispatch("db-1", fakeView(SQL, 12, 12), false);
    expect(dispatch?.databaseId).toBe("db-1");
  });

  it("forwards analyze=false for the primary Explain button", () => {
    const dispatch = buildExplainDispatch("db-1", fakeView(SQL, 12, 12), false);
    expect(dispatch?.analyze).toBe(false);
  });

  it("forwards analyze=true for 'Explain with timings'", () => {
    const dispatch = buildExplainDispatch("db-1", fakeView(SQL, 12, 12), true);
    expect(dispatch?.analyze).toBe(true);
  });
});
