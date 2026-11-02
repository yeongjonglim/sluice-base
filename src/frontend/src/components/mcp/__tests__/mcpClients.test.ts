import { describe, expect, it } from "vitest";
import { MCP_CLIENTS } from "@/components/mcp/mcpClients";

const ctx = { endpoint: "https://acme.example.com/mcp", serverName: "acme-db" };

describe("MCP_CLIENTS", () => {
  it("includes the four expected clients in order", () => {
    expect(MCP_CLIENTS.map((c) => c.id)).toEqual([
      "claude-code",
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

  it("builds a VS Code deeplink containing the endpoint", () => {
    const vscode = MCP_CLIENTS.find((c) => c.id === "vscode");
    const link = vscode?.buildDeeplink?.(ctx) ?? "";
    expect(link).toContain("vscode:mcp/install");
    expect(decodeURIComponent(link)).toContain(ctx.endpoint);
  });

  it("only VS Code provides a deeplink", () => {
    const withDeeplink = MCP_CLIENTS.filter((c) => c.buildDeeplink).map((c) => c.id);
    expect(withDeeplink).toEqual(["vscode"]);
  });
});
