import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { NewUpdateForm, Route } from "@/routes/_authed/update/new.tsx";

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
  useUpdateRequest: () => ({ data: undefined, isPending: false, isError: false }),
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

beforeEach(() => {
  (Route as unknown as Record<string, unknown>).useSearch = vi
    .fn()
    .mockReturnValue({ from: undefined });
});

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

describe("NewUpdateForm — pre-fill via props", () => {
  it("seeds SQL and reason when initial values are provided", () => {
    render(
      React.createElement(NewUpdateForm, {
        initialDatabaseId: "db-abc",
        initialSqlText: "UPDATE public.users SET active = false WHERE id = 42",
        initialReason: "Deactivating stale account per JIRA-999",
      }),
      { wrapper: Wrapper },
    );
    expect(screen.getByTestId("sql-editor")).toHaveValue(
      "UPDATE public.users SET active = false WHERE id = 42",
    );
    expect(screen.getByPlaceholderText(/https:\/\/linear\.app/i)).toHaveValue(
      "Deactivating stale account per JIRA-999",
    );
  });

  it("leaves fields empty when initial values are empty", () => {
    render(
      React.createElement(NewUpdateForm, {
        initialDatabaseId: null,
        initialSqlText: "",
        initialReason: "",
      }),
      { wrapper: Wrapper },
    );
    expect(screen.getByTestId("sql-editor")).toHaveValue("");
    expect(screen.getByPlaceholderText(/https:\/\/linear\.app/i)).toHaveValue("");
  });
});
