import { describe, expect, it } from "vitest";
import { MCP_CLIENTS } from "@/components/mcp/mcpClients";

const ctx = { endpoint: "https://acme.example.com/mcp", serverName: "acme-db" };

describe("MCP_CLIENTS", () => {
  it("includes the five expected clients in order", () => {
    expect(MCP_CLIENTS.map((c) => c.id)).toEqual([
      "claude-code",
      "cursor",
      "vscode",
      "copilot",
      "codex",
    ]);
  });

  it("bakes the endpoint and server name into every snippet", () => {
    for (const client of MCP_CLIENTS) {
      const snippet = client.buildSnippet(ctx);
      expect(snippet).toContain(ctx.endpoint);
      expect(snippet).toContain(ctx.serverName);
    }
  });

  it("builds a Cursor deeplink whose base64 config decodes to the endpoint", () => {
    const cursor = MCP_CLIENTS.find((c) => c.id === "cursor");
    const link = cursor?.buildDeeplink?.(ctx) ?? "";
    expect(link).toContain("cursor://anysphere.cursor-deeplink/mcp/install");

    const config = new URL(link).searchParams.get("config") ?? "";
    const decoded = JSON.parse(atob(config)) as { url: string };
    expect(decoded.url).toBe(ctx.endpoint);
  });

  it("builds a VS Code deeplink containing the endpoint", () => {
    const vscode = MCP_CLIENTS.find((c) => c.id === "vscode");
    const link = vscode?.buildDeeplink?.(ctx) ?? "";
    expect(link).toContain("vscode:mcp/install");
    expect(decodeURIComponent(link)).toContain(ctx.endpoint);
  });

  it("only Cursor and VS Code provide deeplinks", () => {
    const withDeeplink = MCP_CLIENTS.filter((c) => c.buildDeeplink).map((c) => c.id);
    expect(withDeeplink).toEqual(["cursor", "vscode"]);
  });
});
