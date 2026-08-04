import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { Route } from "../update/$id.tsx";
import { ApiError } from "@/api/client";

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

const baseDetail: Record<string, unknown> = {
  id: "req-1",
  databaseId: "db-abc",
  databaseDisplayName: "Prod — users",
  submitterId: "user-1",
  submitterName: "Alice",
  sqlText: "SELECT 1",
  reason: "Checking a fix",
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
  events: [],
};

let mockDetail: Record<string, unknown> = { ...baseDetail };

const mockPreviewMutate = vi.fn();
let mockPreviewState: Record<string, unknown> = {
  mutate: mockPreviewMutate,
  mutateAsync: vi.fn(),
  isPending: false,
  isError: false,
  error: null,
  data: null,
};

vi.mock("@/api/hooks", () => ({
  meQueryOptions: { queryKey: ["me"] },
  useUpdateRequest: () => ({ data: mockDetail, isPending: false, isError: false }),
  useApproveUpdate: () => ({ mutate: vi.fn(), isPending: false }),
  useRejectUpdate: () => ({ mutate: vi.fn(), isPending: false }),
  useCancelUpdate: () => ({ mutate: vi.fn(), isPending: false }),
  useExecuteUpdate: () => ({ mutate: vi.fn(), isPending: false }),
  useSchemaCompletions: () => ({ data: undefined }),
  usePreviewUpdate: () => mockPreviewState,
}));

function makeRouteContext(permissions: Array<string>, id = "user-1") {
  return {
    queryClient: {
      getQueryData: () => ({ permissions, id }),
    },
  };
}

afterEach(() => {
  cleanup();
  mockNavigate.mockReset();
  mockPreviewMutate.mockReset();
  mockDetail = { ...baseDetail };
  mockPreviewState = {
    mutate: mockPreviewMutate,
    mutateAsync: vi.fn(),
    isPending: false,
    isError: false,
    error: null,
    data: null,
  };
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
    .mockReturnValue(makeRouteContext([], "user-1"));
});

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

describe("UpdateDetailPage — Preview", () => {
  it("shows the Preview button for a Pending request when the user can preview", () => {
    render(React.createElement(UpdateDetailPage), { wrapper: Wrapper });
    expect(screen.getByRole("button", { name: /preview/i })).toBeInTheDocument();
  });

  it("calls the preview mutation on click and renders the returned grid", () => {
    mockPreviewMutate.mockImplementation(
      (_id: string, opts: { onSuccess: (data: unknown) => void }) => {
        opts.onSuccess({
          resultSets: [{ columns: ["id", "name"], rows: [["1", "Alice"]] }],
          affectedRows: 1,
          durationMs: 5,
          error: null,
        });
      },
    );

    render(React.createElement(UpdateDetailPage), { wrapper: Wrapper });
    fireEvent.click(screen.getByRole("button", { name: /preview/i }));

    expect(mockPreviewMutate).toHaveBeenCalledWith(
      "req-1",
      expect.objectContaining({ onSuccess: expect.any(Function) }),
    );
    expect(screen.getByText("Result 1")).toBeInTheDocument();
  });

  it("renders the blocked-columns alert on a sensitive-column 403", () => {
    mockPreviewState = {
      mutate: mockPreviewMutate,
      mutateAsync: vi.fn(),
      isPending: false,
      isError: true,
      error: new ApiError(403, {
        type: "sensitive_columns",
        columns: [{ schema: "public", table: "users", column: "ssn" }],
      }),
      data: null,
    };

    render(React.createElement(UpdateDetailPage), { wrapper: Wrapper });

    expect(screen.getByText(/restricted columns/i)).toBeInTheDocument();
    expect(screen.getByText(/public\.users\.ssn/)).toBeInTheDocument();
  });

  it("renders a Previewed event on the timeline", () => {
    mockDetail = {
      ...baseDetail,
      events: [
        {
          type: "Previewed",
          actorId: "user-1",
          actorName: "Alice",
          at: "2026-05-17T00:15:00Z",
          note: null,
          success: true,
          durationMs: 12,
          affectedRows: 3,
          resultSetCount: 1,
          error: null,
        },
      ],
    };

    render(React.createElement(UpdateDetailPage), { wrapper: Wrapper });

    expect(screen.getByText("Previewed")).toBeInTheDocument();
  });
});
