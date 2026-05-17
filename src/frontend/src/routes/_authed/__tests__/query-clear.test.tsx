import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, fireEvent } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import React from "react";

vi.mock("@tanstack/react-router", () => ({
  createFileRoute: () => (opts: unknown) => opts,
  redirect: vi.fn(),
}));

vi.mock("react-resizable-panels", () => ({
  Panel: ({ children }: { children: React.ReactNode }) =>
    React.createElement("div", null, children),
  Group: ({ children }: { children: React.ReactNode }) =>
    React.createElement("div", null, children),
  Separator: () => React.createElement("div"),
}));

vi.mock("@uiw/react-codemirror", () => ({
  default: ({ value, onChange }: { value: string; onChange: (v: string) => void }) =>
    React.createElement("textarea", {
      "data-testid": "editor",
      value,
      onChange: (e: React.ChangeEvent<HTMLTextAreaElement>) => onChange(e.target.value),
    }),
}));

vi.mock("@codemirror/lang-sql", () => ({ sql: () => [] }));
vi.mock("@codemirror/view", () => ({ keymap: { of: () => [] } }));
vi.mock("@codemirror/state", () => ({ Prec: { highest: (x: unknown) => x } }));
vi.mock("@uiw/codemirror-themes-all", () => ({ githubDark: {}, githubLight: {} }));
vi.mock("@/utils/csv.ts", () => ({ exportToCsv: vi.fn() }));
vi.mock("@/utils/sql.ts", () => ({ quoteIdentifier: (s: string) => s }));

vi.mock("@/api/hooks", () => ({
  meQueryOptions: { queryKey: ["me"] },
  useCatalogServer: () => ({ data: { servers: [] } }),
  useSchema: () => ({ isLoading: false, isError: false, data: null }),
  useExecuteQuery: () => ({ mutate: vi.fn(), isPending: false, isError: false, data: null }),
}));

import { QueryPage } from "@/routes/_authed/query/index.tsx";

afterEach(cleanup);

beforeAll(() => {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    value: () => ({ matches: false, addListener: vi.fn(), removeListener: vi.fn(), addEventListener: vi.fn(), removeEventListener: vi.fn(), dispatchEvent: vi.fn() }),
  });
});

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

describe("QueryPage — Clear button", () => {
  it("renders a Clear button that is disabled when the editor is empty", () => {
    render(React.createElement(QueryPage), { wrapper: Wrapper });
    expect(screen.getByRole("button", { name: /clear/i })).toBeDisabled();
  });

  it("enables the Clear button when the editor has content", () => {
    render(React.createElement(QueryPage), { wrapper: Wrapper });
    fireEvent.change(screen.getByTestId("editor"), { target: { value: "SELECT 1" } });
    expect(screen.getByRole("button", { name: /clear/i })).not.toBeDisabled();
  });

  it("empties the editor when Clear is clicked", () => {
    render(React.createElement(QueryPage), { wrapper: Wrapper });
    fireEvent.change(screen.getByTestId("editor"), { target: { value: "SELECT 1" } });
    fireEvent.click(screen.getByRole("button", { name: /clear/i }));
    expect((screen.getByTestId("editor") as HTMLTextAreaElement).value).toBe("");
  });
});
