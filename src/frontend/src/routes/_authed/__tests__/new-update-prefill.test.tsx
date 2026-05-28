import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { NewUpdateForm, Route } from "@/routes/_authed/update/new.tsx";

const NewUpdatePage = (Route as unknown as Record<string, () => React.ReactNode>).component;

const mockRedirect = vi.fn();
const mockNavigate = vi.fn();

vi.mock("@tanstack/react-router", () => ({
  createFileRoute: () => (opts: unknown) => opts,
  redirect: (...args: Array<unknown>) => {
    mockRedirect(...args);
    throw new Error("redirect");
  },
  useNavigate: () => mockNavigate,
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

vi.mock("@codemirror/lang-sql", () => ({ sql: () => [], PostgreSQL: {} }));
vi.mock("@uiw/codemirror-themes-all", () => ({ githubDark: {}, githubLight: {} }));

const mockMutate = vi.fn();
let mockUpdateRequestResult: Record<string, unknown> = { data: undefined, isPending: false, isError: false };

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
  useSubmitUpdate: () => ({ mutate: mockMutate, isPending: false }),
  useUpdateRequest: () => mockUpdateRequestResult,
  useSchemaCompletions: () => ({ data: undefined }),
}));

afterEach(() => {
  cleanup();
  mockNavigate.mockReset();
  mockMutate.mockReset();
  mockRedirect.mockReset();
  mockUpdateRequestResult = { data: undefined, isPending: false, isError: false };
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
  (Route as unknown as Record<string, unknown>).useSearch = vi
    .fn()
    .mockReturnValue({ from: undefined });
});

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

describe("NewUpdateForm — pre-fill via props", () => {
  it("seeds SQL from source and leaves reason empty when sourceRequestId provided", () => {
    render(
      React.createElement(NewUpdateForm, {
        initialDatabaseId: "db-abc",
        initialSqlText: "UPDATE public.users SET active = false WHERE id = 42",
        sourceRequestId: "req-1",
      }),
      { wrapper: Wrapper },
    );
    expect(screen.getByTestId<HTMLTextAreaElement>("sql-editor").value.trim()).toBe(
      "UPDATE public.users SET active = false WHERE id = 42",
    );
    expect(screen.getByPlaceholderText(/https:\/\/example\.com/i)).toHaveValue("");
  });

  it("leaves all fields empty when no source", () => {
    render(
      React.createElement(NewUpdateForm, {
        initialDatabaseId: null,
        initialSqlText: "",
        sourceRequestId: undefined,
      }),
      { wrapper: Wrapper },
    );
    expect(screen.getByTestId<HTMLTextAreaElement>("sql-editor").value.trim()).toBe("");
    expect(screen.getByPlaceholderText(/https:\/\/example\.com/i)).toHaveValue("");
  });
});

describe("NewUpdatePage — route component", () => {
  it("shows loading skeletons when from param is set and source is pending", () => {
    mockUpdateRequestResult = { data: undefined, isPending: true, isError: false };
    (Route as unknown as Record<string, unknown>).useSearch = vi
      .fn()
      .mockReturnValue({ from: "req-1" });
    render(React.createElement(NewUpdatePage), { wrapper: Wrapper });
    expect(screen.queryByTestId("sql-editor")).toBeNull();
    expect(screen.queryByText("New Update Request")).toBeNull();
  });

  it("renders form with source data when loaded", () => {
    mockUpdateRequestResult = {
      data: { databaseId: "db-abc", sqlText: "UPDATE users SET name = 'x'" },
      isPending: false,
      isError: false,
    };
    (Route as unknown as Record<string, unknown>).useSearch = vi
      .fn()
      .mockReturnValue({ from: "req-1" });
    render(React.createElement(NewUpdatePage), { wrapper: Wrapper });
    expect(screen.getByTestId<HTMLTextAreaElement>("sql-editor").value.trim()).toBe("UPDATE users SET name = 'x'");
  });

  it("renders form with empty state when no from param", () => {
    (Route as unknown as Record<string, unknown>).useSearch = vi
      .fn()
      .mockReturnValue({ from: undefined });
    render(React.createElement(NewUpdatePage), { wrapper: Wrapper });
    expect(screen.getByText("New Update Request")).toBeInTheDocument();
    expect(screen.getByTestId<HTMLTextAreaElement>("sql-editor").value.trim()).toBe("");
  });
});

describe("Route.beforeLoad — permission check", () => {
  const beforeLoad = (Route as unknown as { beforeLoad: (ctx: unknown) => void })
    .beforeLoad;

  it("does not redirect when user has update:submit permission", () => {
    const context = { queryClient: { getQueryData: () => ({ permissions: ["update:submit"] }) } };
    expect(() => beforeLoad({ context })).not.toThrow();
  });

  it("redirects when user lacks update:submit permission", () => {
    const context = { queryClient: { getQueryData: () => ({ permissions: [] }) } };
    expect(() => beforeLoad({ context })).toThrow("redirect");
    expect(mockRedirect).toHaveBeenCalledWith({ to: "/" });
  });
});

describe("Route.validateSearch", () => {
  const validateSearch = (Route as unknown as { validateSearch: (s: Record<string, unknown>) => { from?: string } })
    .validateSearch;

  it("extracts from param when it is a string", () => {
    expect(validateSearch({ from: "req-1" })).toEqual({ from: "req-1" });
  });

  it("returns undefined from when not a string", () => {
    expect(validateSearch({ from: 123 })).toEqual({ from: undefined });
    expect(validateSearch({})).toEqual({ from: undefined });
  });
});

describe("NewUpdateForm — submission", () => {
  it("calls mutate with trimmed values on submit", () => {
    render(
      React.createElement(NewUpdateForm, {
        initialDatabaseId: "db-abc",
        initialSqlText: "SELECT 1;\n",
        sourceRequestId: "req-1",
      }),
      { wrapper: Wrapper },
    );

    const reasonInput = screen.getByPlaceholderText(/https:\/\/example\.com/i);
    fireEvent.change(reasonInput, { target: { value: "Fix user data  " } });

    fireEvent.click(screen.getByRole("button", { name: /submit for approval/i }));

    expect(mockMutate).toHaveBeenCalledWith(
      {
        databaseId: "db-abc",
        sqlText: "SELECT 1;",
        reason: "Fix user data",
        sourceRequestId: "req-1",
      },
      expect.objectContaining({ onSuccess: expect.any(Function) }),
    );
  });

  it("does not submit when fields are empty", () => {
    render(
      React.createElement(NewUpdateForm, {
        initialDatabaseId: null,
        initialSqlText: "",
      }),
      { wrapper: Wrapper },
    );

    fireEvent.click(screen.getByRole("button", { name: /submit for approval/i }));
    expect(mockMutate).not.toHaveBeenCalled();
  });

  it("navigates to detail page on successful submit", () => {
    mockMutate.mockImplementation(
      (_args: unknown, opts: { onSuccess: (data: { id: string }) => void }) => {
        opts.onSuccess({ id: "new-req-1" });
      },
    );

    render(
      React.createElement(NewUpdateForm, {
        initialDatabaseId: "db-abc",
        initialSqlText: "UPDATE t SET x = 1",
      }),
      { wrapper: Wrapper },
    );

    const reasonInput = screen.getByPlaceholderText(/https:\/\/example\.com/i);
    fireEvent.change(reasonInput, { target: { value: "reason" } });
    fireEvent.click(screen.getByRole("button", { name: /submit for approval/i }));

    expect(mockNavigate).toHaveBeenCalledWith({
      to: "/update/$id",
      params: { id: "new-req-1" },
    });
  });

  it("updates reason when typing", () => {
    render(
      React.createElement(NewUpdateForm, {
        initialDatabaseId: null,
        initialSqlText: "",
      }),
      { wrapper: Wrapper },
    );

    const reasonInput = screen.getByPlaceholderText(/https:\/\/example\.com/i);
    fireEvent.change(reasonInput, { target: { value: "new reason" } });
    expect(reasonInput).toHaveValue("new reason");
  });
});
