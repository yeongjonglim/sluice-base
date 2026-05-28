import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { SqlEditor } from "@/components/SqlEditor";

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
vi.mock("@codemirror/view", () => ({ EditorView: { lineWrapping: [], theme: () => [] } }));
vi.mock("@/api/hooks", () => ({
  useSchemaCompletions: () => ({ data: undefined }),
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

describe("SqlEditor", () => {
  it("renders with value", () => {
    render(React.createElement(SqlEditor, { value: "SELECT 1" }), { wrapper: Wrapper });
    expect(screen.getByTestId("sql-editor")).toHaveValue("SELECT 1");
  });

  it("renders read-only when editable is false", () => {
    render(React.createElement(SqlEditor, { value: "SELECT 1", editable: false }), {
      wrapper: Wrapper,
    });
    expect(screen.getByTestId("sql-editor")).toHaveAttribute("readonly");
  });

  it("fires onChange callback", () => {
    const onChange = vi.fn();
    render(React.createElement(SqlEditor, { value: "", onChange }), { wrapper: Wrapper });
    const editor = screen.getByTestId("sql-editor");
    editor.dispatchEvent(new Event("change", { bubbles: true }));
  });
});

describe("SqlEditor — minLines padding", () => {
  it("pads value with newlines when below minLines", () => {
    render(React.createElement(SqlEditor, { value: "SELECT 1", minLines: 5 }), {
      wrapper: Wrapper,
    });
    const editor = screen.getByTestId<HTMLTextAreaElement>("sql-editor");
    const lineCount = editor.value.split("\n").length;
    expect(lineCount).toBe(5);
    expect(editor.value).toBe("SELECT 1\n\n\n\n");
  });

  it("does not pad when value already has enough lines", () => {
    const multiLine = "SELECT 1\nFROM t\nWHERE id = 1";
    render(React.createElement(SqlEditor, { value: multiLine, minLines: 3 }), {
      wrapper: Wrapper,
    });
    expect(screen.getByTestId("sql-editor")).toHaveValue(multiLine);
  });

  it("does not pad when minLines is not set", () => {
    render(React.createElement(SqlEditor, { value: "SELECT 1" }), { wrapper: Wrapper });
    expect(screen.getByTestId("sql-editor")).toHaveValue("SELECT 1");
  });
});
