import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { ConnectMcpModal } from "@/components/mcp/ConnectMcpModal";
import { BrandingContext } from "@/theme/BrandingContext";

afterEach(cleanup);

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: () => ({
      matches: false,
      addListener: vi.fn(), removeListener: vi.fn(),
      addEventListener: vi.fn(), removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }),
  });
});

// Match against the rendered <code> block only, so parent elements that also
// contain the text don't cause a multiple-match error. Endpoint is derived from
// jsdom's own origin rather than overriding window.location (which throws in jsdom).
function codeBlockWith(substr: string) {
  return (_: string, el: Element | null) =>
    el?.tagName === "CODE" && el.textContent.includes(substr);
}

function renderModal() {
  const branding = {
    appName: "Acme",
    logoUrl: null,
    faviconUrl: null,
    mcpEnabled: true,
    mcpServerName: "acme-db",
  };
  return render(
    <MantineProvider>
      <BrandingContext value={branding}>
        <ConnectMcpModal opened onClose={vi.fn()} />
      </BrandingContext>
    </MantineProvider>,
  );
}

describe("ConnectMcpModal", () => {
  it("shows a tab per client with the Claude Code snippet using the live endpoint and name", () => {
    renderModal();

    expect(screen.getByRole("tab", { name: /claude code/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /cursor/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /vs code/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /github copilot/i })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: /codex/i })).toBeInTheDocument();

    const endpoint = `${window.location.origin}/mcp`;
    expect(
      screen.getByText(codeBlockWith(`claude mcp add --transport http acme-db ${endpoint}`)),
    ).toBeInTheDocument();
  });

  it("switches the snippet when another client tab is selected", async () => {
    renderModal();
    const user = userEvent.setup();

    await user.click(screen.getByRole("tab", { name: /codex/i }));

    expect(screen.getByText(codeBlockWith("[mcp_servers.acme-db]"))).toBeInTheDocument();
  });

  it("lists the three MCP tools", () => {
    renderModal();

    expect(screen.getByText(/list_databases/)).toBeInTheDocument();
    expect(screen.getByText(/get_schema/)).toBeInTheDocument();
    expect(screen.getByText(/run_query/)).toBeInTheDocument();
  });
});
