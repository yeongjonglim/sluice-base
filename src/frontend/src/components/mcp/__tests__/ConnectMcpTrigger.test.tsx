import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { ConnectMcpTrigger } from "@/components/mcp/ConnectMcpTrigger";
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

function renderTrigger(mcpEnabled: boolean) {
  return render(
    <MantineProvider>
      <BrandingContext
        value={{
          appName: "Acme",
          logoUrl: null,
          faviconUrl: null,
          mcpEnabled,
          mcpServerName: "acme-db",
        }}
      >
        <ConnectMcpTrigger />
      </BrandingContext>
    </MantineProvider>,
  );
}

describe("ConnectMcpTrigger", () => {
  it("renders nothing when MCP is disabled", () => {
    renderTrigger(false);
    expect(screen.queryByLabelText(/connect ai tools/i)).not.toBeInTheDocument();
  });

  it("renders the trigger and opens the modal when MCP is enabled", async () => {
    renderTrigger(true);
    const user = userEvent.setup();

    const button = screen.getByLabelText(/connect ai tools/i);
    expect(button).toBeInTheDocument();

    await user.click(button);
    // Mantine Modal renders into a portal — wait for the dialog to appear in the document
    await waitFor(() =>
      expect(screen.getByRole("tab", { name: /claude code/i })).toBeInTheDocument(),
    );
  });
});
