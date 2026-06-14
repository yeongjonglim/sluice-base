import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { downloadTextFile } from "@/utils/download.ts";

describe("downloadTextFile", () => {
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

  it("sets href and download and clicks the anchor", () => {
    downloadTextFile("CREATE TABLE t();", "schema.sql", "application/sql");
    expect(mockAnchor.href).toBe("blob:fake-url");
    expect(mockAnchor.download).toBe("schema.sql");
    expect(mockAnchor.click).toHaveBeenCalledOnce();
  });

  it("revokes the object URL after clicking", () => {
    downloadTextFile("x", "schema.sql", "application/sql");
    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:fake-url");
  });

  it("creates a blob with the given content and mime type", async () => {
    downloadTextFile("CREATE TABLE t();", "schema.sql", "application/sql");
    const blob = vi.mocked(URL.createObjectURL).mock.calls[0][0] as Blob;
    expect(blob.type).toBe("application/sql");
    expect(await blob.text()).toBe("CREATE TABLE t();");
  });
});
