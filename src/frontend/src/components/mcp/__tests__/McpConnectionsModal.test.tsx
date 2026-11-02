import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { McpConnectionsModal } from "@/components/mcp/McpConnectionsModal";

const mockUseMyMcpGrants = vi.fn();
const mockRevoke = vi.fn();

vi.mock("@/api/hooks", () => ({
  useMyMcpGrants: (...args: Array<unknown>) => mockUseMyMcpGrants(...args),
  useRevokeMyMcpGrant: () => ({ mutate: mockRevoke, isPending: false }),
}));

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

beforeEach(() => vi.clearAllMocks());

function renderModal() {
  return render(
    <MantineProvider>
      <McpConnectionsModal opened onClose={() => {}} />
    </MantineProvider>,
  );
}

describe("McpConnectionsModal", () => {
  it("lists connected clients and revokes the chosen one", async () => {
    mockUseMyMcpGrants.mockReturnValue({
      isLoading: false,
      data: {
        grants: [
          {
            id: "grant-1",
            clientId: "https://claude.ai/oauth/mcp-client.json",
            isLoopback: false,
            signedInAt: "2026-10-01T10:00:00Z",
            lastUsedAt: null,
            expiresAt: "2026-12-30T10:00:00Z",
          },
        ],
      },
    });
    renderModal();

    expect(screen.getByText("https://claude.ai/oauth/mcp-client.json")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Revoke" }));
    expect(mockRevoke).toHaveBeenCalledWith("grant-1");
  });

  it("says so when nothing is connected", () => {
    mockUseMyMcpGrants.mockReturnValue({ isLoading: false, data: { grants: [] } });
    renderModal();
    expect(screen.getByText("No AI tools are connected.")).toBeInTheDocument();
  });
});
