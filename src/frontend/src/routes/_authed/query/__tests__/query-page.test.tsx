import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import React from "react";
import type { ExplainEntry } from "@/api/useExplainRuns";
import type { RunEntry } from "@/api/useQueryRuns";

vi.mock("@tanstack/react-router", () => ({
  createFileRoute: () => (opts: Record<string, unknown>) => opts,
  redirect: (arg: unknown) => ({ __redirect: arg }),
}));

vi.mock("@/api/hooks", () => ({
  meQueryOptions: { queryKey: ["me"] },
  useSchema: () => ({ data: undefined }),
}));

// Shared spies for the fake editor `view`, reset in beforeEach — asserting
// against these confirms handleHighlight/the Ctrl-Enter keymap actually
// reached the view rather than just checking "nothing threw".
const viewDispatch = vi.fn();
const viewFocus = vi.fn();
const viewScrollTo = vi.fn();

// A minimal stand-in for the CodeMirror-backed editor: a controlled textarea
// that exposes just enough of `ReactCodeMirrorRef` (a `view` with the
// doc/selection/dispatch surface `resolveTargetStatements` and
// `highlightStatementInEditor` read) for the page's Run/Explain wiring to
// exercise its real logic without loading CodeMirror.
vi.mock("@/components/SqlEditor", () => ({
  SqlEditor: React.forwardRef(function MockSqlEditor(
    { value, onChange }: { value: string; onChange?: (v: string) => void },
    ref: React.Ref<unknown>,
  ) {
    React.useImperativeHandle(ref, () => ({
      view: {
        state: {
          doc: { toString: () => value },
          selection: { main: { from: 0, to: 0, empty: true } },
        },
        dispatch: viewDispatch,
        focus: viewFocus,
        scrollDOM: { scrollHeight: 0, clientHeight: 0, scrollTop: 0, scrollTo: viewScrollTo },
        lineBlockAt: () => ({ top: 0 }),
      },
    }));
    return React.createElement("textarea", {
      "aria-label": "sql-editor",
      value,
      onChange: (e: React.ChangeEvent<HTMLTextAreaElement>) => onChange?.(e.target.value),
    });
  }),
}));

vi.mock("@/components/DatabaseSelect", () => ({
  DatabaseSelect: ({
    value,
    onChange,
  }: {
    value: string | null;
    onChange: (v: string | null) => void;
  }) =>
    React.createElement(
      "button",
      { type: "button", onClick: () => onChange("db-1") },
      value ?? "Select a database",
    ),
}));

// Renders a single trigger that invokes `onTableClick` with one plain and one
// sensitive column, so the page's `handleTableClick` (which filters out
// sensitive columns and inserts a SELECT snippet) can be exercised without
// the full schema tree UI.
vi.mock("@/components/schema/SchemaSidebar", () => ({
  SchemaSidebar: ({
    onTableClick,
  }: {
    onTableClick: (
      schemaName: string,
      tableName: string,
      columns: Array<{ name: string; isSensitive: boolean; isRestricted: boolean }>,
    ) => void;
  }) =>
    React.createElement(
      "button",
      {
        type: "button",
        onClick: () =>
          onTableClick("public", "orders", [
            { name: "id", isSensitive: false, isRestricted: false },
            { name: "ssn", isSensitive: true, isRestricted: true },
          ]),
      },
      "orders",
    ),
}));

const mockUseQueryRuns = vi.fn();
vi.mock("@/api/useQueryRuns", () => ({
  useQueryRuns: (...a: Array<unknown>) => mockUseQueryRuns(...a),
}));

const mockUseExplainRuns = vi.fn();
vi.mock("@/api/useExplainRuns", () => ({
  useExplainRuns: (...a: Array<unknown>) => mockUseExplainRuns(...a),
}));

// Imported after mocks are registered.
const { QueryPage, Route } = await import("@/routes/_authed/query/index.tsx");

// The mocked `createFileRoute` returns the raw options object, which carries
// `beforeLoad` even though the real `Route`'s static type doesn't surface it.
const RouteOpts = Route as unknown as {
  beforeLoad: (o: {
    context: { queryClient: { getQueryData: () => { permissions: Array<string> } | undefined } };
  }) => void;
};

function explainEntry(over: Partial<ExplainEntry> = {}): ExplainEntry {
  return {
    id: "1-0", index: 0, text: "SELECT 1",
    fromPos: 0, toPos: 8, fromLine: 1, toLine: 1, analyze: false,
    status: "success",
    plan: {
      planJson: "[{}]",
      summary: { totalCost: 1, estimatedRows: 1, rootNode: "Index Scan", hasSeqScan: false, actualTotalMs: null },
    },
    error: null,
    ...over,
  };
}

afterEach(cleanup);

const mockRun = vi.fn();
const mockExplainRun = vi.fn();

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
  mockUseQueryRuns.mockReturnValue({ runs: [], run: mockRun, isRunning: false });
  mockUseExplainRuns.mockReturnValue({ runs: [], run: mockExplainRun, isRunning: false });
});

function runEntry(partial: Partial<RunEntry> & Pick<RunEntry, "id" | "index" | "text">): RunEntry {
  return {
    fromPos: 0, toPos: 8, fromLine: 1, toLine: 1,
    status: "success",
    response: { columns: ["n"], rows: [["1"]], rowCount: 1, durationMs: 1, error: null, estimate: null },
    error: null,
    ...partial,
  };
}

function renderPage() {
  return render(React.createElement(MantineProvider, null, React.createElement(QueryPage)));
}

async function selectDatabaseAndType(sql: string) {
  await userEvent.click(screen.getByText("Select a database"));
  const editor = screen.getByLabelText("sql-editor");
  await userEvent.type(editor, sql);
}

// The Run button's accessible name includes its trailing `<Kbd>` shortcut
// hint (e.g. "RunCtrl+Enter"), so `{ name: "Run" }` can't match it — find it
// by its exact button-label text instead.
function runButton(): HTMLElement {
  return screen.getByText("Run", { selector: "span.mantine-Button-label" }).closest("button")!;
}

describe("QueryPage", () => {
  it("renders the Run, Run all and Explain controls", () => {
    renderPage();
    expect(runButton()).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Run all/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Explain" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Explain options" })).toBeInTheDocument();
  });

  it("disables Run/Explain and prompts for a database until one is selected", () => {
    renderPage();
    expect(runButton()).toBeDisabled();
    expect(screen.getByRole("button", { name: "Explain" })).toBeDisabled();
    expect(screen.getByText(/select a database to run queries/i)).toBeInTheDocument();
  });

  it("shows the results placeholder by default", () => {
    renderPage();
    expect(screen.getByText(/run a query to see results/i)).toBeInTheDocument();
  });

  it("runs the targeted statement when Run is clicked with a database selected", async () => {
    renderPage();
    await selectDatabaseAndType("SELECT 1");
    await userEvent.click(runButton());
    expect(mockRun).toHaveBeenCalledWith(
      "db-1",
      expect.arrayContaining([expect.objectContaining({ text: "SELECT 1" })]),
    );
  });

  it("dispatches Explain and switches the bottom pane to the plan view", async () => {
    mockUseExplainRuns.mockReturnValue({
      runs: [explainEntry()],
      run: mockExplainRun,
      isRunning: false,
    });
    renderPage();
    await selectDatabaseAndType("SELECT 1");

    await userEvent.click(screen.getByRole("button", { name: "Explain" }));

    expect(mockExplainRun).toHaveBeenCalledWith(
      "db-1",
      expect.arrayContaining([expect.objectContaining({ text: "SELECT 1" })]),
      false,
    );
    // The bottom pane switched from ResultTabs to PlanTabs, which renders the
    // mocked explain entry's plan (via PlanView -> PlanSummaryBadges).
    expect(screen.getByText("Index Scan")).toBeInTheDocument();
    expect(screen.queryByText(/run a query to see results/i)).not.toBeInTheDocument();
  });

  it("dispatches Explain with timings from the split-button menu", async () => {
    renderPage();
    await selectDatabaseAndType("SELECT 1");

    await userEvent.click(screen.getByRole("button", { name: "Explain options" }));
    await userEvent.click(await screen.findByText(/Explain with timings/));

    expect(mockExplainRun).toHaveBeenCalledWith(
      "db-1",
      expect.arrayContaining([expect.objectContaining({ text: "SELECT 1" })]),
      true,
    );
  });

  it("does not dispatch a run when no statement is present", async () => {
    renderPage();
    await userEvent.click(screen.getByText("Select a database"));
    // No SQL typed — the editor is empty — so Run stays disabled and clicking
    // Explain (also disabled) dispatches nothing.
    expect(runButton()).toBeDisabled();
    expect(mockRun).not.toHaveBeenCalled();
    expect(mockExplainRun).not.toHaveBeenCalled();
  });

  it("runs every statement when Run all is clicked", async () => {
    renderPage();
    await selectDatabaseAndType("SELECT 1; SELECT 2;");
    await userEvent.click(screen.getByRole("button", { name: /Run all/ }));
    expect(mockRun).toHaveBeenCalledWith(
      "db-1",
      expect.arrayContaining([
        expect.objectContaining({ text: "SELECT 1" }),
        expect.objectContaining({ text: "SELECT 2" }),
      ]),
    );
  });

  it("inserts a SELECT snippet for the clicked table, excluding sensitive columns", async () => {
    renderPage();
    await userEvent.click(screen.getByText("orders"));
    const editor = screen.getByLabelText<HTMLTextAreaElement>("sql-editor");
    expect(editor.value).toContain("SELECT id");
    expect(editor.value).toContain("FROM public.orders");
    expect(editor.value).not.toContain("ssn");
  });

  it("highlights the target statement in the editor when a result tab is clicked", async () => {
    mockUseQueryRuns.mockReturnValue({
      runs: [
        runEntry({ id: "1-0", index: 0, text: "SELECT a" }),
        runEntry({ id: "1-1", index: 1, text: "SELECT b", fromPos: 10, toPos: 18 }),
      ],
      run: mockRun,
      isRunning: false,
    });
    renderPage();
    await userEvent.click(screen.getByText(/SELECT b/));
    expect(viewDispatch).toHaveBeenCalledWith({ selection: { anchor: 10, head: 18 } });
    expect(viewFocus).toHaveBeenCalled();
  });
});

describe("Route.beforeLoad", () => {
  function context(permissions: Array<string>) {
    return { context: { queryClient: { getQueryData: () => ({ permissions }) } } };
  }

  it("allows users that have query:execute", () => {
    expect(() => RouteOpts.beforeLoad(context(["query:execute"]))).not.toThrow();
  });

  it("redirects users without query:execute", () => {
    expect(() => RouteOpts.beforeLoad(context([]))).toThrow();
  });
});
