import { describe, expect, it } from "vitest";
import { splitSqlStatements } from "@/utils/splitSqlStatements";
import { selectStatements } from "@/utils/selectStatements";

// "SELECT 1;\nSELECT 2;\nSELECT 3"
//  s0 pos 0..8   s1 pos 10..18   s2 pos 20..28
const SQL = "SELECT 1;\nSELECT 2;\nSELECT 3";
const stmts = splitSqlStatements(SQL);

describe("selectStatements", () => {
  it("returns all statements when runAll is true", () => {
    const r = selectStatements(stmts, { from: 0, to: 0, empty: true }, true);
    expect(r.map((s) => s.text)).toEqual(["SELECT 1", "SELECT 2", "SELECT 3"]);
  });

  it("returns the statement under the cursor when selection is empty", () => {
    const r = selectStatements(stmts, { from: 12, to: 12, empty: true }, false);
    expect(r.map((s) => s.text)).toEqual(["SELECT 2"]);
  });

  it("expands a partial selection to the whole intersecting statement", () => {
    // caret range wholly inside s1 ("LECT" of SELECT 2)
    const r = selectStatements(stmts, { from: 12, to: 15, empty: false }, false);
    expect(r.map((s) => s.text)).toEqual(["SELECT 2"]);
  });

  it("returns every statement a multi-statement selection touches", () => {
    // from inside s0 to inside s2
    const r = selectStatements(stmts, { from: 3, to: 24, empty: false }, false);
    expect(r.map((s) => s.text)).toEqual(["SELECT 1", "SELECT 2", "SELECT 3"]);
  });

  it("falls back to the preceding statement when the cursor sits after a terminator", () => {
    // pos 9 is the '\n' right after s0's ';' — no statement contains it
    const r = selectStatements(stmts, { from: 9, to: 9, empty: true }, false);
    expect(r.map((s) => s.text)).toEqual(["SELECT 1"]);
  });

  it("returns [] for no statements", () => {
    expect(selectStatements([], { from: 0, to: 0, empty: true }, false)).toEqual([]);
    expect(selectStatements([], { from: 0, to: 0, empty: true }, true)).toEqual([]);
  });
});
