import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
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

vi.mock("@codemirror/lang-sql", () => ({ sql: () => [], PostgreSQL: {} }));
vi.mock("@uiw/codemirror-themes-all", () => ({ githubDark: {}, githubLight: {} }));
vi.mock("@mantine/modals", () => ({ modals: { openConfirmModal: vi.fn() } }));

const PREVIEW_EVENT = {
  type: "Previewed",
  actorId: "user-1",
  actorName: "Alice",
  at: "2026-05-17T00:10:00Z",
  note: null,
  success: true,
  durationMs: 8,
  affectedRows: 3,
  resultSetCount: 0,
  error: null,
};

const baseDetail: Record<string, unknown> = {
  id: "req-1",
  databaseId: "db-abc",
  databaseDisplayName: "Prod — users",
  submitterId: "user-1",
  submitterName: "Alice",
  sqlText: "UPDATE public.users SET active = false WHERE id = 42",
  reason: "Deactivating stale account",
  status: "Pending",
  reviewerId: null,
  reviewerName: null,
  reviewNote: null,
  cancelledById: null,
  cancelledByName: null,
  cancelNote: null,
  executorId: null,
  executorName: null,
  submittedAt: "2026-05-17T00:00:00Z",
  reviewedAt: null,
  executedAt: null,
  cancelledAt: null,
  execSuccess: null,
  execDurationMs: null,
  execAffectedRows: null,
  execError: null,
  sourceRequestId: null,
  events: [PREVIEW_EVENT],
};

let mockDetail: Record<string, unknown> = { ...baseDetail };
const mockApprove = vi.fn();

vi.mock("@/api/hooks", () => ({
  meQueryOptions: { queryKey: ["me"] },
  useUpdateRequest: () => ({ data: mockDetail, isPending: false, isError: false }),
  useApproveUpdate: () => ({ mutate: mockApprove, isPending: false }),
  useRejectUpdate: () => ({ mutate: vi.fn(), isPending: false }),
  useCancelUpdate: () => ({ mutate: vi.fn(), isPending: false }),
  useExecuteUpdate: () => ({ mutate: vi.fn(), isPending: false }),
  useSchemaCompletions: () => ({ data: undefined }),
  usePreviewUpdate: () => ({
    mutate: vi.fn(),
    isPending: false,
    isError: false,
    error: null,
    data: null,
  }),
}));

function makeRouteContext(permissions: Array<string>) {
  return { queryClient: { getQueryData: () => ({ id: "user-1", permissions }) } };
}

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

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
    .mockReturnValue(makeRouteContext(["update:approve", "update:submit"]));
});

afterEach(() => {
  cleanup();
  mockApprove.mockReset();
  mockDetail = { ...baseDetail };
});

describe("UpdateDetailPage — approval safety", () => {
  it("blocks approval after a failed dry run until the reviewer overrides it", async () => {
    mockDetail = {
      ...baseDetail,
      events: [
        {
          ...PREVIEW_EVENT,
          success: false,
          affectedRows: null,
          error: 'relation "shipment" does not exist',
        },
      ],
    };
    render(React.createElement(UpdateDetailPage), { wrapper: Wrapper });

    // The header states the failure before any action is taken.
    expect(screen.getAllByText(/last dry run failed/i).length).toBeGreaterThan(0);

    fireEvent.click(screen.getByRole("button", { name: /^approve$/i }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/relation "shipment" does not exist/i)).toBeInTheDocument();

    fireEvent.change(within(dialog).getByLabelText(/note/i), {
      target: { value: "Checked with the on-call DBA" },
    });
    const confirm = within(dialog).getByRole("button", { name: /confirm approve/i });
    expect(confirm).toBeDisabled();

    fireEvent.click(within(dialog).getByLabelText(/without a successful dry run/i));
    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);
    expect(mockApprove).toHaveBeenCalledWith(
      { id: "req-1", note: "Checked with the on-call DBA" },
      expect.anything(),
    );
  });

  it("approves without an override when the last dry run passed", async () => {
    render(React.createElement(UpdateDetailPage), { wrapper: Wrapper });
    expect(screen.queryByText(/last dry run failed/i)).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: /^approve$/i }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).queryByLabelText(/without a successful dry run/i)).toBeNull();

    fireEvent.change(within(dialog).getByLabelText(/note/i), {
      target: { value: "Verified against staging" },
    });
    expect(within(dialog).getByRole("button", { name: /confirm approve/i })).toBeEnabled();
  });

  it("does not carry a rejection note into the approval dialog", async () => {
    render(React.createElement(UpdateDetailPage), { wrapper: Wrapper });

    fireEvent.click(screen.getByRole("button", { name: /^reject$/i }));
    const rejectDialog = await screen.findByRole("dialog");
    fireEvent.change(within(rejectDialog).getByLabelText(/note/i), {
      target: { value: "Targets the wrong table" },
    });
    fireEvent.click(within(rejectDialog).getByRole("button", { name: /back/i }));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());

    fireEvent.click(screen.getByRole("button", { name: /^approve$/i }));
    const approveDialog = await screen.findByRole("dialog");
    expect(within(approveDialog).getByLabelText(/note/i)).toHaveValue("");
    expect(within(approveDialog).getByRole("button", { name: /confirm approve/i })).toBeDisabled();
  });
});
