import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { autoFitWidth, columnWidths } from "@/components/query/columnWidths";

// jsdom has no layout engine, so measureRenderedText's getBoundingClientRect
// returns 0 and it falls back to the char-count estimate (6.6px/char); stubbing
// getContext forces measureText onto the same fallback. Both paths agree, so the
// widths below are deterministic.
beforeEach(() => {
  vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(null);
});
afterEach(() => vi.restoreAllMocks());

describe("columnWidths", () => {
  it("clamps a short column up to the minimum width", () => {
    // "id" -> ceil(2*6.6)=14, +12 chrome = 26, clamped up to MIN 32.
    expect(columnWidths(["id"], [["1"]])).toEqual([32]);
  });

  it("clamps a very wide column down to the maximum width", () => {
    // 80 chars -> ceil(528)+12=540, clamped down to MAX 360.
    expect(columnWidths(["c"], [["x".repeat(80)]])).toEqual([360]);
  });

  it("sizes to the header when it is wider than the cells", () => {
    // 23-char header -> ceil(151.8)=152, +12 = 164; wider than the "x" cell.
    expect(columnWidths(["a_very_long_header_name"], [["x"]])).toEqual([164]);
  });

  it("treats null cells as the text 'NULL' without throwing", () => {
    // "NULL" -> ceil(4*6.6)=27, +12 = 39; above the MIN 32 floor.
    expect(columnWidths(["c"], [[null]])).toEqual([39]);
  });
});

describe("autoFitWidth", () => {
  it("fits past the 360 estimate cap, up to the manual maximum", () => {
    // Same 540px content: columnWidths clamps to 360, autoFit keeps 540.
    expect(columnWidths(["c"], [["x".repeat(80)]])).toEqual([360]);
    expect(autoFitWidth(["c"], [["x".repeat(80)]], 0)).toBe(540);
  });

  it("clamps to the manual maximum of 800", () => {
    // 200 chars -> ceil(1320)+12 = 1332, clamped to 800.
    expect(autoFitWidth(["c"], [["x".repeat(200)]], 0)).toBe(800);
  });
});
