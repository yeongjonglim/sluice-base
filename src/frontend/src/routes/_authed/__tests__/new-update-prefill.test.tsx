import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { NewUpdatePage } from "@/routes/_authed/update/new.tsx";

vi.mock("@tanstack/react-router", () => ({
  createFileRoute: () => (opts: unknown) => opts,
  redirect: vi.fn(),
  useNavigate: () => vi.fn(),
}));

vi.mock("@uiw/react-codemirror", () => ({
  default: ({ value, onChange }: { value: string; onChange?: (v: string) => void }) =>
    React.createElement("textarea", {
      "data-testid": "sql-editor",
      value,
      readOnly: !onChange,
      onChange: onChange
        ? (e: React.ChangeEvent<HTMLTextAreaElement>) => onChange(e.target.value)
        : undefined,
    }),
}));

vi.mock("@codemirror/lang-sql", () => ({ sql: () => [] }));
vi.mock("@uiw/codemirror-themes-all", () => ({ githubDark: {}, githubLight: {} }));

const fakeDetail = {
  id: "req-1",
  databaseId: "db-abc",
  databaseDisplayName: "Prod — users",
  submitterId: "user-1",
  submitterName: "Alice",
  sqlText: "UPDATE public.users SET active = false WHERE id = 42",
  reason: "Deactivating stale account per JIRA-999",
  status: "Executed",
  reviewerId: null,
  reviewerName: null,
  reviewNote: null,
  cancelledById: null,
  cancelledByName: null,
  cancelNote: null,
  executorId: "user-2",
  executorName: "Bob",
  submittedAt: "2026-05-17T00:00:00Z",
  reviewedAt: null,
  executedAt: "2026-05-17T01:00:00Z",
  cancelledAt: null,
  execSuccess: false,
  execDurationMs: 120,
  execAffectedRows: null,
  execError: 'column "active" does not exist',
};

vi.mock("@/api/hooks", () => ({
  meQueryOptions: { queryKey: ["me"] },
  useCatalogServer: () => ({
    data: {
      servers: [
        {
          name: "Prod",
          databases: [{ id: "db-abc", displayName: "users", canWrite: true }],
        },
      ],
    },
  }),
  useSubmitUpdate: () => ({ mutate: vi.fn(), isPending: false }),
  useUpdateRequest: (id: string) => ({
    data: id === "req-1" ? fakeDetail : undefined,
    isPending: false,
    isError: false,
  }),
}));

afterEach(cleanup);

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

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

describe("NewUpdatePage — ?from pre-fill", () => {
  it("seeds SQL and reason from the source request when ?from is provided", async () => {
    render(React.createElement(NewUpdatePage, { searchFrom: "req-1" }), { wrapper: Wrapper });
    await waitFor(() => {
      expect((screen.getByTestId("sql-editor") as HTMLTextAreaElement).value).toBe(
        "UPDATE public.users SET active = false WHERE id = 42",
      );
    });
    expect(
      (screen.getByPlaceholderText(/https:\/\/linear\.app/i) as HTMLTextAreaElement).value,
    ).toBe("Deactivating stale account per JIRA-999");
  });

  it("leaves fields empty when no ?from param", () => {
    render(React.createElement(NewUpdatePage, { searchFrom: undefined }), { wrapper: Wrapper });
    expect((screen.getByTestId("sql-editor") as HTMLTextAreaElement).value).toBe("");
    expect(
      (screen.getByPlaceholderText(/https:\/\/linear\.app/i) as HTMLTextAreaElement).value,
    ).toBe("");
  });
});
