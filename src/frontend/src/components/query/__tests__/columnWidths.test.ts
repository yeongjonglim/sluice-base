import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { autoFitWidth, columnWidths } from "@/components/query/columnWidths";

// Force measureText's char-count fallback (6.6px/char) so widths are deterministic.
beforeEach(() => {
  vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(null);
});
afterEach(() => vi.restoreAllMocks());

describe("columnWidths", () => {
  it("clamps a short column up to the minimum width", () => {
    // "id" -> ceil(2*6.6)=14, +24 chrome = 38, clamped up to MIN 56.
    expect(columnWidths(["id"], [["1"]])).toEqual([56]);
  });

  it("clamps a very wide column down to the maximum width", () => {
    // 80 chars -> ceil(528)+24=552, clamped down to MAX 360.
    expect(columnWidths(["c"], [["x".repeat(80)]])).toEqual([360]);
  });

  it("sizes to the header when it is wider than the cells", () => {
    // 23-char header -> ceil(151.8)=152, +24 = 176; wider than the "x" cell.
    expect(columnWidths(["a_very_long_header_name"], [["x"]])).toEqual([176]);
  });

  it("treats null cells as the text 'NULL' without throwing", () => {
    expect(columnWidths(["c"], [[null]])).toEqual([56]);
  });
});

describe("autoFitWidth", () => {
  it("fits past the 360 estimate cap, up to the manual maximum", () => {
    // Same 552px content: columnWidths clamps to 360, autoFit keeps 552.
    expect(columnWidths(["c"], [["x".repeat(80)]])).toEqual([360]);
    expect(autoFitWidth(["c"], [["x".repeat(80)]], 0)).toBe(552);
  });

  it("clamps to the manual maximum of 800", () => {
    // 200 chars -> ceil(1320)+24 = 1344, clamped to 800.
    expect(autoFitWidth(["c"], [["x".repeat(200)]], 0)).toBe(800);
  });
});
