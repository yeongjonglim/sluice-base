import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { buildCsv, exportToCsv } from "../query";

describe("buildCsv", () => {
  it("produces a header row and data rows", () => {
    const result = buildCsv(["id", "name"], [["1", "Alice"], ["2", "Bob"]]);
    expect(result).toBe("id,name\n1,Alice\n2,Bob");
  });

  it("renders null and undefined cells as empty strings", () => {
    const result = buildCsv(["a", "b"], [[null, undefined]]);
    expect(result).toBe("a,b\n,");
  });

  it("wraps values containing commas in double-quotes", () => {
    const result = buildCsv(["v"], [["hello, world"]]);
    expect(result).toBe('v\n"hello, world"');
  });

  it("escapes double-quotes inside quoted values by doubling them", () => {
    const result = buildCsv(["v"], [['say "hi"']]);
    expect(result).toBe('v\n"say ""hi"""');
  });

  it("wraps values containing newlines in double-quotes", () => {
    const result = buildCsv(["v"], [["line1\nline2"]]);
    expect(result).toBe('v\n"line1\nline2"');
  });

  it("produces only the header row when rows array is empty", () => {
    const result = buildCsv(["col1", "col2"], []);
    expect(result).toBe("col1,col2");
  });
});

describe("exportToCsv", () => {
  const mockAnchor = {
    href: "",
    download: "",
    click: vi.fn(),
  };

  beforeEach(() => {
    const originalCreateElement = document.createElement.bind(document);
    vi.stubGlobal("URL", {
      createObjectURL: vi.fn(() => "blob:fake-url"),
      revokeObjectURL: vi.fn(),
    });
    vi.spyOn(document, "createElement").mockImplementation((tag: string) => {
      if (tag === "a") return mockAnchor as unknown as HTMLElement;
      return originalCreateElement(tag);
    });
    mockAnchor.href = "";
    mockAnchor.download = "";
    mockAnchor.click.mockClear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("sets href, download, and calls click", () => {
    exportToCsv(["id"], [["1"]], "results.csv");
    expect(mockAnchor.href).toBe("blob:fake-url");
    expect(mockAnchor.download).toBe("results.csv");
    expect(mockAnchor.click).toHaveBeenCalledOnce();
  });

  it("revokes the object URL after clicking", () => {
    exportToCsv(["id"], [["1"]], "results.csv");
    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:fake-url");
  });
});
