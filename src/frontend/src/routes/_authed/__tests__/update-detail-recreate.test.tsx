import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { Route } from "../update/$id.tsx";

const UpdateDetailPage = (Route as unknown as Record<string, () => React.ReactNode>).component;

const mockNavigate = vi.fn();

vi.mock("@tanstack/react-router", () => ({
  createFileRoute: () => (opts: unknown) => opts,
  redirect: vi.fn(),
  useNavigate: () => mockNavigate,
}));

vi.mock("@uiw/react-codemirror", () => ({
  default: ({ value }: { value: string }) =>
    React.createElement("textarea", { "data-testid": "sql-editor", value, readOnly: true }),
}));

vi.mock("@codemirror/lang-sql", () => ({ sql: () => [] }));
vi.mock("@uiw/codemirror-themes-all", () => ({ githubDark: {}, githubLight: {} }));
vi.mock("@mantine/modals", () => ({ modals: { openConfirmModal: vi.fn() } }));

const fakeDetail = {
  id: "req-1",
  databaseId: "db-abc",
  databaseDisplayName: "Prod — users",
  submitterId: "user-1",
  submitterName: "Alice",
  sqlText: "UPDATE public.users SET active = false WHERE id = 42",
  reason: "Deactivating stale account",
  status: "Executed",
  reviewerId: "user-2",
  reviewerName: "Bob",
  reviewNote: "Approved",
  cancelledById: null,
  cancelledByName: null,
  cancelNote: null,
  executorId: "user-2",
  executorName: "Bob",
  submittedAt: "2026-05-17T00:00:00Z",
  reviewedAt: "2026-05-17T00:30:00Z",
  executedAt: "2026-05-17T01:00:00Z",
  cancelledAt: null,
  execSuccess: false,
  execDurationMs: 120,
  execAffectedRows: null,
  execError: 'column "active" does not exist',
};

vi.mock("@/api/hooks", () => ({
  meQueryOptions: { queryKey: ["me"] },
  useUpdateRequest: () => ({ data: fakeDetail, isPending: false, isError: false }),
  useApproveUpdate: () => ({ mutate: vi.fn(), isPending: false }),
  useRejectUpdate: () => ({ mutate: vi.fn(), isPending: false }),
  useCancelUpdate: () => ({ mutate: vi.fn(), isPending: false }),
  useExecuteUpdate: () => ({ mutate: vi.fn(), isPending: false }),
}));

function makeRouteContext(permissions: Array<string>) {
  return {
    queryClient: {
      getQueryData: () => ({ permissions }),
    },
  };
}

afterEach(() => {
  cleanup();
  mockNavigate.mockReset();
});

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: () => ({
      matches: false,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }),
  });
});

beforeEach(() => {
  (Route as unknown as Record<string, unknown>).useParams = vi.fn().mockReturnValue({ id: "req-1" });
  (Route as unknown as Record<string, unknown>).useRouteContext = vi
    .fn()
    .mockReturnValue(makeRouteContext([]));
});

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

describe("UpdateDetailPage — Recreate button", () => {
  it("shows Recreate button when user has update:submit", () => {
    (Route as unknown as Record<string, unknown>).useRouteContext = vi
      .fn()
      .mockReturnValue(makeRouteContext(["update:submit"]));
    render(React.createElement(UpdateDetailPage), { wrapper: Wrapper });
    expect(screen.getByRole("button", { name: /recreate/i })).toBeInTheDocument();
  });

  it("hides Recreate button when user lacks update:submit", () => {
    render(React.createElement(UpdateDetailPage), { wrapper: Wrapper });
    expect(screen.queryByRole("button", { name: /recreate/i })).toBeNull();
  });

  it("navigates to /update/new?from=<id> when Recreate is clicked", () => {
    (Route as unknown as Record<string, unknown>).useRouteContext = vi
      .fn()
      .mockReturnValue(makeRouteContext(["update:submit"]));
    render(React.createElement(UpdateDetailPage), { wrapper: Wrapper });
    fireEvent.click(screen.getByRole("button", { name: /recreate/i }));
    expect(mockNavigate).toHaveBeenCalledWith({
      to: "/update/new",
      search: { from: "req-1" },
    });
  });
});
