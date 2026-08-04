import { afterEach, describe, expect, it, vi } from "vitest";
import { measureText } from "@/utils/measureText";

afterEach(() => vi.restoreAllMocks());

describe("measureText", () => {
  it("falls back to a per-character estimate when no 2D context is available", () => {
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(null);
    expect(measureText("hello", "12px sans-serif")).toBeCloseTo(5 * 6.6);
  });

  it("uses the canvas 2D context when one is available", () => {
    const ctx = { font: "", measureText: (t: string) => ({ width: t.length * 10 }) };
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(
      ctx as unknown as CanvasRenderingContext2D,
    );
    expect(measureText("hi", "10px sans-serif")).toBe(20);
  });
});
