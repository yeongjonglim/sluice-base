# Query Playground — Multi-Statement Execution & Result Tabs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the query page run a highlighted selection, the statement under the cursor, or every statement in the editor, showing each statement's result as its own tab whose label identifies the source statement and which highlights the originating lines on click.

**Architecture:** Frontend-only. Split the editor SQL into statements client-side, fire one `POST /api/query` per statement (parallel, capped at 6 concurrent), and render each result in a Mantine tab. All run paths (selection / cursor / all) converge on a single `SqlStatement` shape carrying absolute editor coordinates, so tab→editor highlighting is uniform. No backend, engine, OpenAPI, or `schema.ts` changes.

**Tech Stack:** React 19 + TypeScript, Mantine v9, TanStack Router, `@uiw/react-codemirror` (`@codemirror/*`), Vitest + `@testing-library/react`.

## Global Constraints

- Use `Array<T>` instead of `T[]` (ESLint `@typescript-eslint/array-type`).
- Frontend work only; do **not** modify any `.cs` file, `openapi.json`, or `src/api/schema.ts`.
- All frontend commands run from the `src/frontend` directory.
- Run a single test file with `npx vitest run <path>`; typecheck with `npx tsc -b`; lint with `npm run lint`.
- Import from the `@/` alias (e.g. `@/api/client`), matching existing files.
- Preserve existing comments unless factually wrong.
- Branch is already `feat/query-playground-multi-result` (off `main`). Commit per task.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/utils/splitSqlStatements.ts` (new) | Pure splitter: SQL string → `Array<SqlStatement>` with absolute char/line ranges. |
| `src/utils/selectStatements.ts` (new) | Pure: choose which statements a run targets, from a selection/cursor or "all". |
| `src/utils/runLimited.ts` (new) | Pure concurrency helper: run async workers with a max in-flight cap. |
| `src/utils/editorHighlight.ts` (new) | Select + scroll a CodeMirror `EditorView` to a char range. |
| `src/api/useQueryRuns.ts` (new) | Hook owning `Array<RunEntry>`; dispatches capped-parallel `/api/query` calls; patches per-entry status. |
| `src/components/query/ResultGrid.tsx` (new) | Renders one `RunEntry` (grid / query-error / blocked-columns / CSV), extracted from today's `QueryResults`. |
| `src/components/query/ResultTabs.tsx` (new) | Tabs from `Array<RunEntry>`; snippet labels + error dots + tooltip; click → highlight callback; hosts active `ResultGrid`. |
| `src/routes/_authed/query/index.tsx` (modify) | Wire splitter + selector + hook + keymaps + tabs + highlight. Remove the old single-result path. |

Types defined once and reused:
- `SqlStatement` — exported from `splitSqlStatements.ts`.
- `RunEntry` — exported from `useQueryRuns.ts`.

---

## Task 1: SQL statement splitter

**Files:**
- Create: `src/utils/splitSqlStatements.ts`
- Test: `src/utils/__tests__/splitSqlStatements.test.ts`

**Interfaces:**
- Consumes: nothing.
- Produces:
  ```ts
  export interface SqlStatement {
    text: string;      // trimmed statement SQL (no trailing ';')
    fromPos: number;   // absolute char offset of first non-ws char
    toPos: number;     // absolute char offset just past last non-ws char
    fromLine: number;  // 1-based line of fromPos
    toLine: number;    // 1-based line of toPos
  }
  export function splitSqlStatements(sql: string): Array<SqlStatement>;
  ```

- [ ] **Step 1: Write the failing tests**

```ts
// src/utils/__tests__/splitSqlStatements.test.ts
import { describe, expect, it } from "vitest";
import { splitSqlStatements } from "@/utils/splitSqlStatements";

describe("splitSqlStatements", () => {
  it("returns empty array for blank input", () => {
    expect(splitSqlStatements("   \n\t ")).toEqual([]);
  });

  it("ignores comment-only input", () => {
    expect(splitSqlStatements("-- just a comment\n/* block */")).toEqual([]);
  });

  it("splits on semicolons and trims each statement", () => {
    const s = splitSqlStatements("SELECT 1;\nSELECT 2;");
    expect(s.map((x) => x.text)).toEqual(["SELECT 1", "SELECT 2"]);
  });

  it("keeps a trailing statement without a terminator", () => {
    const s = splitSqlStatements("SELECT 1;\nSELECT 2");
    expect(s.map((x) => x.text)).toEqual(["SELECT 1", "SELECT 2"]);
  });

  it("does not split on a semicolon inside a string literal", () => {
    const s = splitSqlStatements("SELECT ';' AS a; SELECT 2");
    expect(s.map((x) => x.text)).toEqual(["SELECT ';' AS a", "SELECT 2"]);
  });

  it("handles doubled single-quote escapes inside a literal", () => {
    const s = splitSqlStatements("SELECT 'it''s; ok' AS a; SELECT 2");
    expect(s.map((x) => x.text)).toEqual(["SELECT 'it''s; ok' AS a", "SELECT 2"]);
  });

  it("does not split inside a $$ dollar-quoted body", () => {
    const s = splitSqlStatements("DO $$ BEGIN raise notice ';'; END $$; SELECT 2");
    expect(s.map((x) => x.text)).toEqual([
      "DO $$ BEGIN raise notice ';'; END $$",
      "SELECT 2",
    ]);
  });

  it("does not split inside a tagged $tag$ dollar-quoted body", () => {
    const s = splitSqlStatements("SELECT $body$a;b$body$ AS x; SELECT 2");
    expect(s.map((x) => x.text)).toEqual(["SELECT $body$a;b$body$ AS x", "SELECT 2"]);
  });

  it("does not split on a semicolon inside a line comment", () => {
    const s = splitSqlStatements("SELECT 1 -- a;b\n; SELECT 2");
    expect(s.map((x) => x.text)).toEqual(["SELECT 1 -- a;b", "SELECT 2"]);
  });

  it("does not split on a semicolon inside a block comment", () => {
    const s = splitSqlStatements("SELECT 1 /* a;b */; SELECT 2");
    expect(s.map((x) => x.text)).toEqual(["SELECT 1 /* a;b */", "SELECT 2"]);
  });

  it("reports absolute char and 1-based line ranges", () => {
    const sql = "SELECT 1;\nSELECT 2;";
    const s = splitSqlStatements(sql);
    expect(s[0]).toMatchObject({ fromPos: 0, toPos: 8, fromLine: 1, toLine: 1 });
    expect(s[1]).toMatchObject({ fromPos: 10, toPos: 18, fromLine: 2, toLine: 2 });
    expect(sql.slice(s[1].fromPos, s[1].toPos)).toBe("SELECT 2");
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx vitest run src/utils/__tests__/splitSqlStatements.test.ts`
Expected: FAIL — `splitSqlStatements` is not exported / module not found.

- [ ] **Step 3: Write the implementation**

```ts
// src/utils/splitSqlStatements.ts
export interface SqlStatement {
  text: string;
  fromPos: number;
  toPos: number;
  fromLine: number;
  toLine: number;
}

type Mode = "normal" | "single" | "lineComment" | "blockComment" | "dollar";

// At a '$', match a dollar-quote opener like $$ or $tag$ and return the full
// tag (including both $). Returns null when it's not a dollar quote (e.g. $1).
function matchDollarTag(sql: string, i: number): string | null {
  let j = i + 1;
  while (j < sql.length && /[A-Za-z0-9_]/.test(sql[j])) j++;
  return sql[j] === "$" ? sql.slice(i, j + 1) : null;
}

function lineAt(sql: string, pos: number): number {
  let line = 1;
  for (let k = 0; k < pos && k < sql.length; k++) {
    if (sql[k] === "\n") line++;
  }
  return line;
}

export function splitSqlStatements(sql: string): Array<SqlStatement> {
  const statements: Array<SqlStatement> = [];
  const n = sql.length;
  let segStart = 0;
  let mode: Mode = "normal";
  let dollarTag = "";

  const pushSegment = (rawStart: number, rawEnd: number) => {
    const raw = sql.slice(rawStart, rawEnd);
    const trimmed = raw.trim();
    if (trimmed.length === 0) return;
    const fromPos = rawStart + (raw.length - raw.trimStart().length);
    const toPos = fromPos + trimmed.length;
    statements.push({
      text: trimmed,
      fromPos,
      toPos,
      fromLine: lineAt(sql, fromPos),
      toLine: lineAt(sql, toPos),
    });
  };

  let i = 0;
  while (i < n) {
    const c = sql[i];
    const next = sql[i + 1];

    if (mode === "normal") {
      if (c === "'") { mode = "single"; i++; continue; }
      if (c === "-" && next === "-") { mode = "lineComment"; i += 2; continue; }
      if (c === "/" && next === "*") { mode = "blockComment"; i += 2; continue; }
      if (c === "$") {
        const tag = matchDollarTag(sql, i);
        if (tag) { dollarTag = tag; mode = "dollar"; i += tag.length; continue; }
      }
      if (c === ";") {
        pushSegment(segStart, i); // exclude the ';'
        i++;
        segStart = i;
        continue;
      }
      i++;
      continue;
    }

    if (mode === "single") {
      if (c === "'") {
        if (next === "'") { i += 2; continue; } // escaped quote
        mode = "normal"; i++; continue;
      }
      i++; continue;
    }

    if (mode === "lineComment") {
      if (c === "\n") mode = "normal";
      i++; continue;
    }

    if (mode === "blockComment") {
      if (c === "*" && next === "/") { mode = "normal"; i += 2; continue; }
      i++; continue;
    }

    // mode === "dollar"
    if (c === "$" && sql.startsWith(dollarTag, i)) {
      mode = "normal"; i += dollarTag.length; continue;
    }
    i++;
  }

  pushSegment(segStart, n); // trailing statement (no terminator)
  return statements;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx vitest run src/utils/__tests__/splitSqlStatements.test.ts`
Expected: PASS (11 tests).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/utils/splitSqlStatements.ts src/frontend/src/utils/__tests__/splitSqlStatements.test.ts
git commit -m "Add SQL statement splitter for query playground"
```

---

## Task 2: Statement selection resolver

**Files:**
- Create: `src/utils/selectStatements.ts`
- Test: `src/utils/__tests__/selectStatements.test.ts`

**Interfaces:**
- Consumes: `SqlStatement` from `@/utils/splitSqlStatements`.
- Produces:
  ```ts
  export interface EditorSelection { from: number; to: number; empty: boolean }
  // runAll=true -> every statement.
  // else selection (from<to) -> statements whose range intersects it (a partly
  //   highlighted statement is included in full).
  // else cursor (empty) -> the statement containing the cursor, falling back to
  //   the last statement starting at/before the cursor, else the first.
  export function selectStatements(
    statements: Array<SqlStatement>,
    selection: EditorSelection,
    runAll: boolean,
  ): Array<SqlStatement>;
  ```

- [ ] **Step 1: Write the failing tests**

```ts
// src/utils/__tests__/selectStatements.test.ts
import { describe, expect, it } from "vitest";
import { splitSqlStatements } from "@/utils/splitSqlStatements";
import { selectStatements } from "@/utils/selectStatements";

// "SELECT 1;\nSELECT 2;\nSELECT 3"
//  s0 pos 0..8   s1 pos 10..18   s2 pos 20..28
const SQL = "SELECT 1;\nSELECT 2;\nSELECT 3";
const stmts = splitSqlStatements(SQL);

describe("selectStatements", () => {
  it("returns all statements when runAll is true", () => {
    const r = selectStatements(stmts, { from: 0, to: 0, empty: true }, true);
    expect(r.map((s) => s.text)).toEqual(["SELECT 1", "SELECT 2", "SELECT 3"]);
  });

  it("returns the statement under the cursor when selection is empty", () => {
    const r = selectStatements(stmts, { from: 12, to: 12, empty: true }, false);
    expect(r.map((s) => s.text)).toEqual(["SELECT 2"]);
  });

  it("expands a partial selection to the whole intersecting statement", () => {
    // caret range wholly inside s1 ("LECT" of SELECT 2)
    const r = selectStatements(stmts, { from: 12, to: 15, empty: false }, false);
    expect(r.map((s) => s.text)).toEqual(["SELECT 2"]);
  });

  it("returns every statement a multi-statement selection touches", () => {
    // from inside s0 to inside s2
    const r = selectStatements(stmts, { from: 3, to: 24, empty: false }, false);
    expect(r.map((s) => s.text)).toEqual(["SELECT 1", "SELECT 2", "SELECT 3"]);
  });

  it("falls back to the preceding statement when the cursor sits after a terminator", () => {
    // pos 9 is the '\n' right after s0's ';' — no statement contains it
    const r = selectStatements(stmts, { from: 9, to: 9, empty: true }, false);
    expect(r.map((s) => s.text)).toEqual(["SELECT 1"]);
  });

  it("returns [] for no statements", () => {
    expect(selectStatements([], { from: 0, to: 0, empty: true }, false)).toEqual([]);
    expect(selectStatements([], { from: 0, to: 0, empty: true }, true)).toEqual([]);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx vitest run src/utils/__tests__/selectStatements.test.ts`
Expected: FAIL — `selectStatements` not found.

- [ ] **Step 3: Write the implementation**

```ts
// src/utils/selectStatements.ts
import type { SqlStatement } from "@/utils/splitSqlStatements";

export interface EditorSelection {
  from: number;
  to: number;
  empty: boolean;
}

export function selectStatements(
  statements: Array<SqlStatement>,
  selection: EditorSelection,
  runAll: boolean,
): Array<SqlStatement> {
  if (statements.length === 0) return [];
  if (runAll) return statements;

  if (!selection.empty) {
    // Any statement whose range overlaps the selection, in full.
    return statements.filter(
      (s) => s.fromPos < selection.to && s.toPos > selection.from,
    );
  }

  const pos = selection.from;
  const containing = statements.find((s) => s.fromPos <= pos && pos <= s.toPos);
  if (containing) return [containing];

  // Cursor sits between statements: use the last one starting at/before it,
  // else the first statement.
  const preceding = [...statements].reverse().find((s) => s.fromPos <= pos);
  return [preceding ?? statements[0]];
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx vitest run src/utils/__tests__/selectStatements.test.ts`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/utils/selectStatements.ts src/frontend/src/utils/__tests__/selectStatements.test.ts
git commit -m "Add statement selection resolver for query playground"
```

---

## Task 3: Bounded-concurrency helper

**Files:**
- Create: `src/utils/runLimited.ts`
- Test: `src/utils/__tests__/runLimited.test.ts`

**Interfaces:**
- Consumes: nothing.
- Produces:
  ```ts
  // Runs worker over every item, never more than `limit` in flight at once.
  // Resolves after all items complete.
  export function runLimited<T>(
    items: Array<T>,
    limit: number,
    worker: (item: T, index: number) => Promise<void>,
  ): Promise<void>;
  ```

- [ ] **Step 1: Write the failing tests**

```ts
// src/utils/__tests__/runLimited.test.ts
import { describe, expect, it } from "vitest";
import { runLimited } from "@/utils/runLimited";

function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>((r) => (resolve = r));
  return { promise, resolve };
}

describe("runLimited", () => {
  it("runs every item exactly once, preserving index", async () => {
    const seen: Array<number> = [];
    await runLimited([10, 20, 30], 2, async (item, index) => {
      seen.push(item + index);
    });
    expect(seen.sort((a, b) => a - b)).toEqual([10, 21, 32]);
  });

  it("never exceeds the concurrency limit", async () => {
    const gates = [deferred(), deferred(), deferred(), deferred()];
    let inFlight = 0;
    let maxInFlight = 0;

    const run = runLimited([0, 1, 2, 3], 2, async (i) => {
      inFlight++;
      maxInFlight = Math.max(maxInFlight, inFlight);
      await gates[i].promise;
      inFlight--;
    });

    // Let the first wave start, then release gates one at a time.
    await Promise.resolve();
    gates[0].resolve();
    gates[1].resolve();
    gates[2].resolve();
    gates[3].resolve();
    await run;

    expect(maxInFlight).toBeLessThanOrEqual(2);
  });

  it("resolves immediately for an empty list", async () => {
    await expect(runLimited([], 3, async () => {})).resolves.toBeUndefined();
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `npx vitest run src/utils/__tests__/runLimited.test.ts`
Expected: FAIL — `runLimited` not found.

- [ ] **Step 3: Write the implementation**

```ts
// src/utils/runLimited.ts
export async function runLimited<T>(
  items: Array<T>,
  limit: number,
  worker: (item: T, index: number) => Promise<void>,
): Promise<void> {
  let cursor = 0;
  const workerCount = Math.max(1, Math.min(limit, items.length));
  const runners = Array.from({ length: workerCount }, async () => {
    while (cursor < items.length) {
      const index = cursor++;
      await worker(items[index], index);
    }
  });
  await Promise.all(runners);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx vitest run src/utils/__tests__/runLimited.test.ts`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/utils/runLimited.ts src/frontend/src/utils/__tests__/runLimited.test.ts
git commit -m "Add bounded-concurrency helper for query playground"
```

---

## Task 4: Editor highlight helper

**Files:**
- Create: `src/utils/editorHighlight.ts`
- Test: `src/utils/__tests__/editorHighlight.test.ts`

**Interfaces:**
- Consumes: `EditorView` from `@codemirror/view`.
- Produces:
  ```ts
  // Selects [from, to] in the editor and scrolls it into view, so the tab's
  // source statement is highlighted. No-op safe in jsdom (scroll is a no-op).
  export function highlightStatementInEditor(
    view: EditorView, from: number, to: number,
  ): void;
  ```

- [ ] **Step 1: Write the failing test**

```ts
// src/utils/__tests__/editorHighlight.test.ts
import { afterEach, describe, expect, it } from "vitest";
import { EditorView } from "@codemirror/view";
import { EditorState } from "@codemirror/state";
import { highlightStatementInEditor } from "@/utils/editorHighlight";

let view: EditorView;
afterEach(() => view?.destroy());

describe("highlightStatementInEditor", () => {
  it("selects the given range in the editor", () => {
    view = new EditorView({
      state: EditorState.create({ doc: "SELECT 1;\nSELECT 2;" }),
      parent: document.body,
    });
    highlightStatementInEditor(view, 10, 18);
    expect(view.state.selection.main.from).toBe(10);
    expect(view.state.selection.main.to).toBe(18);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/utils/__tests__/editorHighlight.test.ts`
Expected: FAIL — `highlightStatementInEditor` not found.

- [ ] **Step 3: Write the implementation**

```ts
// src/utils/editorHighlight.ts
import type { EditorView } from "@codemirror/view";

export function highlightStatementInEditor(
  view: EditorView,
  from: number,
  to: number,
): void {
  view.dispatch({
    selection: { anchor: from, head: to },
    scrollIntoView: true,
  });
  view.focus();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/utils/__tests__/editorHighlight.test.ts`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/utils/editorHighlight.ts src/frontend/src/utils/__tests__/editorHighlight.test.ts
git commit -m "Add editor highlight helper for query playground"
```

---

## Task 5: useQueryRuns hook

**Files:**
- Create: `src/api/useQueryRuns.ts`
- Test: `src/api/__tests__/useQueryRuns.test.ts`

**Interfaces:**
- Consumes: `apiRequest`, `ApiError` from `@/api/client`; `ExecuteQueryResponse` from `@/api/hooks`; `SqlStatement` from `@/utils/splitSqlStatements`; `runLimited` from `@/utils/runLimited`.
- Produces:
  ```ts
  export interface RunEntry {
    id: string;
    index: number;
    text: string;
    fromPos: number;
    toPos: number;
    fromLine: number;
    toLine: number;
    status: "pending" | "success" | "error" | "blocked";
    response: ExecuteQueryResponse | null;
    error: unknown;
  }
  export function useQueryRuns(): {
    runs: Array<RunEntry>;
    run: (databaseId: string, statements: Array<SqlStatement>) => void;
    isRunning: boolean;
  };
  ```

**Notes on status mapping:**
- 200 response with `response.error == null` → `"success"`.
- 200 response with `response.error != null` (query-level error) → `"error"` (keep `response`).
- `ApiError` 403 with body `type === "sensitive_columns"` → `"blocked"` (keep `error`).
- Any other thrown error → `"error"` (keep `error`, `response` null).
- A newer `run()` supersedes older in-flight batches; stale patches are ignored.

- [ ] **Step 1: Write the failing test**

```ts
// src/api/__tests__/useQueryRuns.test.ts
import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useQueryRuns } from "@/api/useQueryRuns";
import type { SqlStatement } from "@/utils/splitSqlStatements";

vi.mock("@/api/client", async () => {
  const actual = await vi.importActual<typeof import("@/api/client")>("@/api/client");
  return { ...actual, apiRequest: vi.fn() };
});
import { apiRequest } from "@/api/client";

const mockApiRequest = vi.mocked(apiRequest);

function stmt(text: string, fromPos: number): SqlStatement {
  return { text, fromPos, toPos: fromPos + text.length, fromLine: 1, toLine: 1 };
}

beforeEach(() => mockApiRequest.mockReset());

describe("useQueryRuns", () => {
  it("creates one pending entry per statement, then resolves each to success", async () => {
    mockApiRequest.mockResolvedValue({
      columns: ["n"], rows: [["1"]], rowCount: 1, durationMs: 3, error: null,
    });

    const { result } = renderHook(() => useQueryRuns());
    act(() => result.current.run("db-1", [stmt("SELECT 1", 0), stmt("SELECT 2", 10)]));

    expect(result.current.runs).toHaveLength(2);
    await waitFor(() =>
      expect(result.current.runs.every((r) => r.status === "success")).toBe(true),
    );
    expect(mockApiRequest).toHaveBeenCalledTimes(2);
    expect(result.current.isRunning).toBe(false);
  });

  it("marks a query-level error (200 with error text) as error but keeps the response", async () => {
    mockApiRequest.mockResolvedValue({
      columns: null, rows: null, rowCount: 0, durationMs: 2, error: "syntax error",
    });
    const { result } = renderHook(() => useQueryRuns());
    act(() => result.current.run("db-1", [stmt("SELEC 1", 0)]));
    await waitFor(() => expect(result.current.runs[0].status).toBe("error"));
    expect(result.current.runs[0].response?.error).toBe("syntax error");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/api/__tests__/useQueryRuns.test.ts`
Expected: FAIL — `useQueryRuns` not found.

- [ ] **Step 3: Write the implementation**

```ts
// src/api/useQueryRuns.ts
import { useCallback, useRef, useState } from "react";
import { ApiError, apiRequest } from "@/api/client";
import type { ExecuteQueryResponse } from "@/api/hooks";
import type { SqlStatement } from "@/utils/splitSqlStatements";
import { runLimited } from "@/utils/runLimited";
import type { paths } from "@/api/schema";

const MAX_CONCURRENCY = 6;

type QueryRequestBody =
  paths["/api/query"]["post"]["requestBody"]["content"]["application/json"];

export interface RunEntry {
  id: string;
  index: number;
  text: string;
  fromPos: number;
  toPos: number;
  fromLine: number;
  toLine: number;
  status: "pending" | "success" | "error" | "blocked";
  response: ExecuteQueryResponse | null;
  error: unknown;
}

function isBlocked(err: unknown): boolean {
  return (
    err instanceof ApiError &&
    err.status === 403 &&
    (err.body as { type?: string } | null)?.type === "sensitive_columns"
  );
}

export function useQueryRuns() {
  const [runs, setRuns] = useState<Array<RunEntry>>([]);
  const batchRef = useRef(0);

  const run = useCallback((databaseId: string, statements: Array<SqlStatement>) => {
    const batchId = ++batchRef.current;

    const initial: Array<RunEntry> = statements.map((s, index) => ({
      id: `${batchId}-${index}`,
      index,
      text: s.text,
      fromPos: s.fromPos,
      toPos: s.toPos,
      fromLine: s.fromLine,
      toLine: s.toLine,
      status: "pending",
      response: null,
      error: null,
    }));
    setRuns(initial);

    const patch = (id: string, update: Partial<RunEntry>) => {
      // Ignore results from a superseded batch.
      if (batchId !== batchRef.current) return;
      setRuns((prev) => prev.map((r) => (r.id === id ? { ...r, ...update } : r)));
    };

    void runLimited(initial, MAX_CONCURRENCY, async (entry) => {
      try {
        const response = await apiRequest<QueryRequestBody, ExecuteQueryResponse>(
          "/api/query",
          { method: "POST", body: { databaseId, sql: entry.text } },
        );
        patch(entry.id, {
          status: response.error ? "error" : "success",
          response,
          error: null,
        });
      } catch (err) {
        patch(entry.id, {
          status: isBlocked(err) ? "blocked" : "error",
          response: null,
          error: err,
        });
      }
    });
  }, []);

  const isRunning = runs.some((r) => r.status === "pending");
  return { runs, run, isRunning };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/api/__tests__/useQueryRuns.test.ts`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/api/useQueryRuns.ts src/frontend/src/api/__tests__/useQueryRuns.test.ts
git commit -m "Add useQueryRuns hook for parallel multi-statement execution"
```

---

## Task 6: ResultGrid component

**Files:**
- Create: `src/components/query/ResultGrid.tsx`
- Test: `src/components/query/__tests__/ResultGrid.test.tsx`

**Interfaces:**
- Consumes: `RunEntry` from `@/api/useQueryRuns`; `ApiError` from `@/api/client`; `exportToCsv` from `@/utils/csv`.
- Produces:
  ```ts
  export function ResultGrid({ entry }: { entry: RunEntry }): React.JSX.Element;
  ```

**Behavior (mirrors today's `QueryResults` branches, per single entry):**
- `status === "pending"` → three skeleton bars.
- `status === "blocked"` → orange "restricted columns" alert listing `error.body.columns`.
- `status === "error"` with a `response` → red "Query error" alert with `response.error` and `durationMs`.
- `status === "error"` without a `response` → red "Request failed" alert.
- `status === "success"` → stats row (row count · durationMs) + CSV button + sticky-header table.

- [ ] **Step 1: Write the failing test**

```tsx
// src/components/query/__tests__/ResultGrid.test.tsx
import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import { ResultGrid } from "@/components/query/ResultGrid";
import type { RunEntry } from "@/api/useQueryRuns";

function base(): RunEntry {
  return {
    id: "1-0", index: 0, text: "SELECT 1", fromPos: 0, toPos: 8,
    fromLine: 1, toLine: 1, status: "success", response: null, error: null,
  };
}

function renderGrid(entry: RunEntry) {
  return render(
    <MantineProvider>
      <ResultGrid entry={entry} />
    </MantineProvider>,
  );
}

describe("ResultGrid", () => {
  it("renders a table with columns and rows for a successful result", () => {
    renderGrid({
      ...base(),
      status: "success",
      response: { columns: ["id", "name"], rows: [["1", "Ada"]], rowCount: 1, durationMs: 5, error: null },
    });
    expect(screen.getByText("id")).toBeInTheDocument();
    expect(screen.getByText("Ada")).toBeInTheDocument();
    expect(screen.getByText(/1 row/)).toBeInTheDocument();
  });

  it("renders a query error alert", () => {
    renderGrid({
      ...base(),
      status: "error",
      response: { columns: null, rows: null, rowCount: 0, durationMs: 2, error: "boom" },
    });
    expect(screen.getByText("boom")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/components/query/__tests__/ResultGrid.test.tsx`
Expected: FAIL — `ResultGrid` not found.

- [ ] **Step 3: Write the implementation**

```tsx
// src/components/query/ResultGrid.tsx
import {
  Alert, Button, Code, Flex, Group, ScrollArea, Skeleton, Stack, Table, Text,
} from "@mantine/core";
import { IconDownload } from "@tabler/icons-react";
import { ApiError } from "@/api/client";
import { exportToCsv } from "@/utils/csv.ts";
import type { RunEntry } from "@/api/useQueryRuns";

export function ResultGrid({ entry }: { entry: RunEntry }) {
  if (entry.status === "pending") {
    return (
      <Stack p="xs" gap="xs">
        {[1, 2, 3].map((i) => (
          <Skeleton key={i} h={24} radius="sm" />
        ))}
      </Stack>
    );
  }

  if (entry.status === "blocked") {
    const apiErr = entry.error instanceof ApiError ? entry.error : null;
    const body = apiErr?.body as {
      columns?: Array<{ schema: string; table: string; column: string }>;
    } | null;
    return (
      <Alert color="orange" title="Query blocked — restricted columns" m="xs">
        <Text size="sm" mb="xs">
          Your query references columns you are not authorised to access:
        </Text>
        {(body?.columns ?? []).map((c, i) => (
          <Code key={i} display="block" fz="xs">
            {c.schema}.{c.table}.{c.column}
          </Code>
        ))}
      </Alert>
    );
  }

  if (entry.status === "error") {
    if (entry.response?.error) {
      return (
        <Stack p="xs" gap="xs">
          <Text size="xs" c="dimmed">
            Error · {entry.response.durationMs} ms
          </Text>
          <Alert color="red" title="Query error">
            {entry.response.error}
          </Alert>
        </Stack>
      );
    }
    return (
      <Alert color="red" title="Request failed" m="xs">
        Could not reach the server. Check your connection and try again.
      </Alert>
    );
  }

  // success
  const columns = entry.response?.columns ?? [];
  const rows = entry.response?.rows ?? [];
  const rowCount = entry.response?.rowCount ?? 0;

  return (
    <Flex direction="column" style={{ height: "100%" }}>
      <Group
        justify="space-between"
        align="center"
        px="xs"
        style={{
          flexShrink: 0,
          height: 32,
          borderBottom: "1px solid var(--mantine-color-default-border)",
        }}
      >
        <Text size="xs" c="dimmed">
          {rowCount} {rowCount === 1 ? "row" : "rows"} · {entry.response?.durationMs} ms
        </Text>
        <Button
          size="xs"
          variant="subtle"
          leftSection={<IconDownload size={12} />}
          onClick={() => exportToCsv(columns, rows, `query-results-${entry.index + 1}.csv`)}
        >
          CSV
        </Button>
      </Group>
      <ScrollArea style={{ flex: 1, minHeight: 0 }} type="auto">
        <Table
          stickyHeader
          striped
          withTableBorder
          withColumnBorders
          fz="xs"
          style={{ whiteSpace: "nowrap" }}
        >
          <Table.Thead>
            <Table.Tr>
              {columns.map((col) => (
                <Table.Th key={col}>{col}</Table.Th>
              ))}
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {rows.map((row, i) => (
              <Table.Tr key={i}>
                {row.map((cell, j) => (
                  <Table.Td key={j}>
                    {cell === null ? (
                      <Text size="xs" c="dimmed" fs="italic">NULL</Text>
                    ) : cell}
                  </Table.Td>
                ))}
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </ScrollArea>
    </Flex>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/components/query/__tests__/ResultGrid.test.tsx`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/components/query/ResultGrid.tsx src/frontend/src/components/query/__tests__/ResultGrid.test.tsx
git commit -m "Add ResultGrid component for per-statement query results"
```

---

## Task 7: ResultTabs component

**Files:**
- Create: `src/components/query/ResultTabs.tsx`
- Test: `src/components/query/__tests__/ResultTabs.test.tsx`

**Interfaces:**
- Consumes: `RunEntry` from `@/api/useQueryRuns`; `ResultGrid` from `@/components/query/ResultGrid`.
- Produces:
  ```ts
  export function ResultTabs({
    runs,
    onHighlight,
  }: {
    runs: Array<RunEntry>;
    onHighlight: (entry: RunEntry) => void;
  }): React.JSX.Element;
  ```

**Behavior:**
- Empty `runs` → dimmed "Run a query to see results." placeholder.
- One tab per entry, ordered by `fromPos` (entries already come ordered).
- Tab label: `snippet(text)` (first ~36 chars, single line) + row count when success, or a red dot when `error`/`blocked`. Tooltip shows the full statement text.
- Active tab defaults to the first entry; clicking a tab activates it **and** calls `onHighlight(entry)`.
- Active entry rendered via `ResultGrid`.

- [ ] **Step 1: Write the failing test**

```tsx
// src/components/query/__tests__/ResultTabs.test.tsx
import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MantineProvider } from "@mantine/core";
import { ResultTabs } from "@/components/query/ResultTabs";
import type { RunEntry } from "@/api/useQueryRuns";

function entry(partial: Partial<RunEntry> & Pick<RunEntry, "id" | "index" | "text">): RunEntry {
  return {
    fromPos: 0, toPos: 8, fromLine: 1, toLine: 1,
    status: "success",
    response: { columns: ["n"], rows: [["1"]], rowCount: 1, durationMs: 1, error: null },
    error: null,
    ...partial,
  };
}

function renderTabs(runs: Array<RunEntry>, onHighlight = vi.fn()) {
  render(
    <MantineProvider>
      <ResultTabs runs={runs} onHighlight={onHighlight} />
    </MantineProvider>,
  );
  return onHighlight;
}

describe("ResultTabs", () => {
  it("shows a placeholder when there are no runs", () => {
    renderTabs([]);
    expect(screen.getByText(/Run a query to see results/)).toBeInTheDocument();
  });

  it("renders a tab per run with a statement snippet", () => {
    renderTabs([
      entry({ id: "1-0", index: 0, text: "SELECT a FROM t1" }),
      entry({ id: "1-1", index: 1, text: "SELECT b FROM t2" }),
    ]);
    expect(screen.getByText(/SELECT a FROM t1/)).toBeInTheDocument();
    expect(screen.getByText(/SELECT b FROM t2/)).toBeInTheDocument();
  });

  it("calls onHighlight with the entry when its tab is clicked", async () => {
    const onHighlight = renderTabs([
      entry({ id: "1-0", index: 0, text: "SELECT a" }),
      entry({ id: "1-1", index: 1, text: "SELECT b" }),
    ]);
    await userEvent.click(screen.getByText(/SELECT b/));
    expect(onHighlight).toHaveBeenCalledWith(expect.objectContaining({ id: "1-1" }));
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/components/query/__tests__/ResultTabs.test.tsx`
Expected: FAIL — `ResultTabs` not found.

- [ ] **Step 3: Write the implementation**

```tsx
// src/components/query/ResultTabs.tsx
import { useEffect, useState } from "react";
import { Box, Group, Tabs, Text, Tooltip } from "@mantine/core";
import { ResultGrid } from "@/components/query/ResultGrid";
import type { RunEntry } from "@/api/useQueryRuns";

function snippet(text: string, max = 36): string {
  const oneLine = text.replace(/\s+/g, " ").trim();
  return oneLine.length > max ? `${oneLine.slice(0, max - 1)}…` : oneLine;
}

function TabLabel({ entry }: { entry: RunEntry }) {
  const failed = entry.status === "error" || entry.status === "blocked";
  return (
    <Tooltip label={entry.text} multiline maw={480} withArrow openDelay={400}>
      <Group gap={6} wrap="nowrap">
        {failed && (
          <Box
            w={7}
            h={7}
            style={{ borderRadius: "50%", background: "var(--mantine-color-red-6)", flexShrink: 0 }}
          />
        )}
        <Text size="xs" style={{ fontFamily: "var(--mantine-font-family-monospace)" }}>
          {snippet(entry.text)}
        </Text>
        {entry.status === "success" && (
          <Text size="xs" c="dimmed">
            {entry.response?.rowCount ?? 0}
          </Text>
        )}
      </Group>
    </Tooltip>
  );
}

export function ResultTabs({
  runs,
  onHighlight,
}: {
  runs: Array<RunEntry>;
  onHighlight: (entry: RunEntry) => void;
}) {
  const [active, setActive] = useState<string | null>(runs[0]?.id ?? null);

  // When a new run batch replaces the tabs, default to the first tab.
  useEffect(() => {
    if (runs.length > 0 && !runs.some((r) => r.id === active)) {
      setActive(runs[0].id);
    }
  }, [runs, active]);

  if (runs.length === 0) {
    return (
      <Text p="xs" size="sm" c="dimmed">
        Run a query to see results.
      </Text>
    );
  }

  const activeEntry = runs.find((r) => r.id === active) ?? runs[0];

  return (
    <Tabs
      value={activeEntry.id}
      onChange={(value) => {
        if (!value) return;
        setActive(value);
        const entry = runs.find((r) => r.id === value);
        if (entry) onHighlight(entry);
      }}
      keepMounted={false}
      style={{ display: "flex", flexDirection: "column", height: "100%" }}
    >
      <Tabs.List style={{ flexShrink: 0, flexWrap: "nowrap", overflowX: "auto" }}>
        {runs.map((entry) => (
          <Tabs.Tab key={entry.id} value={entry.id}>
            <TabLabel entry={entry} />
          </Tabs.Tab>
        ))}
      </Tabs.List>
      <Box style={{ flex: 1, minHeight: 0 }}>
        <ResultGrid entry={activeEntry} />
      </Box>
    </Tabs>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/components/query/__tests__/ResultTabs.test.tsx`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/components/query/ResultTabs.tsx src/frontend/src/components/query/__tests__/ResultTabs.test.tsx
git commit -m "Add ResultTabs component with statement provenance"
```

---

## Task 8: Wire the query route

**Files:**
- Modify: `src/routes/_authed/query/index.tsx`

**Interfaces:**
- Consumes: `splitSqlStatements` (`@/utils/splitSqlStatements`), `selectStatements` (`@/utils/selectStatements`), `highlightStatementInEditor` (`@/utils/editorHighlight`), `useQueryRuns` (`@/api/useQueryRuns`), `ResultTabs` (`@/components/query/ResultTabs`).
- Produces: no new exports (route component only).

**What changes:**
- Replace `useExecuteQuery` + inline `QueryResults` with `useQueryRuns` + `ResultTabs`.
- `handleRun(runAll)` reads the editor view's selection, splits + resolves the target statements, and calls `run(...)`.
- `Cmd/Ctrl+Enter` → `handleRun(false)`; `Shift+Cmd/Ctrl+Enter` → `handleRun(true)`.
- Run button `loading` = `isRunning`; disabled when no DB or the editor has no statements.
- `onHighlight` maps a tab back to its editor range via `editorRef`.
- Delete the entire `QueryResults` function (moved into `ResultGrid`).

- [ ] **Step 1: Update imports**

In `src/routes/_authed/query/index.tsx`, replace the import of `useExecuteQuery` and remove now-unused symbols. Change line 34:

```ts
import { meQueryOptions, useSchema } from "@/api/hooks";
```

Add these imports alongside the existing component imports (after line 36):

```ts
import { useQueryRuns } from "@/api/useQueryRuns";
import { splitSqlStatements } from "@/utils/splitSqlStatements";
import { selectStatements } from "@/utils/selectStatements";
import { highlightStatementInEditor } from "@/utils/editorHighlight";
import { ResultTabs } from "@/components/query/ResultTabs";
```

Remove these imports that only `QueryResults` used: `Alert`, `Code`, `Flex`, `ScrollArea`, `Skeleton`, `Table`, `IconDownload`, `ExecuteQueryResponse` (type), `ApiError`, `exportToCsv`. Keep `ActionIcon`, `Box`, `Button`, `Group`, `Kbd`, `Popover`, `Splitter`, `Stack`, `Text`, `IconPlayerPlay`, `IconQuestionMark`.

- [ ] **Step 2: Replace the hook usage and handlers in `QueryPage`**

Replace the body from `const executeQuery = useExecuteQuery();` (line 75) through the end of `editorExtensions` (line 112) with:

```tsx
  const { runs, run, isRunning } = useQueryRuns();

  const handleTableClick = useCallback(
    (schemaName: string, tableName: string, columns: Array<{ name: string; isSensitive: boolean; isRestricted: boolean }>) => {
      const safeCols = columns.filter((c) => !c.isSensitive);
      if (safeCols.length === 0) return;
      const colList = safeCols.map((c) => c.name).join(", ");
      const snippet = `SELECT ${colList}\nFROM ${schemaName}.${tableName}\nLIMIT 1000;\n`;
      setEditorContent((prev) =>
        prev.trimEnd() === "" ? snippet : `${prev.trimEnd()}\n\n${snippet}`,
      );
    },
    [setEditorContent],
  );

  const statements = useMemo(() => splitSqlStatements(editorContent), [editorContent]);

  const handleRun = useCallback(
    (runAll: boolean) => {
      if (!selectedDatabaseId || statements.length === 0) return;
      const view = editorRef.current?.view;
      const sel = view
        ? {
            from: view.state.selection.main.from,
            to: view.state.selection.main.to,
            empty: view.state.selection.main.empty,
          }
        : { from: 0, to: 0, empty: true };
      const targets = selectStatements(statements, sel, runAll);
      if (targets.length > 0) run(selectedDatabaseId, targets);
    },
    [selectedDatabaseId, statements, run],
  );

  const handleHighlight = useCallback((entry: { fromPos: number; toPos: number }) => {
    const view = editorRef.current?.view;
    if (view) highlightStatementInEditor(view, entry.fromPos, entry.toPos);
  }, []);

  const runKeymap = Prec.highest(
    keymap.of([
      {
        key: "Ctrl-Enter",
        mac: "Cmd-Enter",
        run: () => {
          handleRun(false);
          return true;
        },
      },
      {
        key: "Shift-Ctrl-Enter",
        mac: "Shift-Cmd-Enter",
        run: () => {
          handleRun(true);
          return true;
        },
      },
    ]),
  );

  const editorExtensions = useMemo(
    () => [runKeymap, noIndentKeymap],
    [runKeymap],
  );
```

Also delete the old `handleTableClick` and `handleRun` definitions (lines 77–107) that this replaces — the block above supplies the new versions.

- [ ] **Step 3: Update the Run button and add the "Run all" hint**

Replace the `<Button …>Run</Button>` (lines 179–188) with:

```tsx
                <Button
                  leftSection={<IconPlayerPlay size={14} />}
                  rightSection={<Kbd size="xs">{isMac ? "⌘" : "Ctrl"}+Enter</Kbd>}
                  size="sm"
                  onClick={() => handleRun(false)}
                  loading={isRunning}
                  disabled={!selectedDatabaseId || statements.length === 0}
                >
                  Run
                </Button>
```

In the keyboard-shortcuts `Popover.Dropdown` `Stack` (after the "Run query" row, line 198), add:

```tsx
                      <Group gap="xs" justify="space-between"><Text size="xs">Run all statements</Text><Kbd size="xs">Shift+{isMac ? "⌘" : "Ctrl"}+Enter</Kbd></Group>
```

- [ ] **Step 4: Swap the results pane**

Replace the `<QueryResults … />` block (lines 218–223) with:

```tsx
            <ResultTabs runs={runs} onHighlight={handleHighlight} />
```

- [ ] **Step 5: Delete the old `QueryResults` function**

Remove the entire `function QueryResults({ … }) { … }` definition (lines 231–362) — its logic now lives in `ResultGrid`.

- [ ] **Step 6: Typecheck, lint, and run the full test suite**

Run:
```bash
npx tsc -b
npm run lint
npx vitest run
```
Expected: `tsc` clean (no unused-import or type errors), lint clean, all tests pass. If `tsc` flags an unused import, remove it (Step 1 lists the ones to drop).

- [ ] **Step 7: Manual verification in the running app**

Start the app (via the project's Aspire/dev flow) and on `/query`:
1. Pick a database. Type `SELECT 1; SELECT 2; SELECT 3;` and press the Run-all shortcut (`Shift+Cmd/Ctrl+Enter`) → three tabs appear, each labelled with its statement snippet.
2. Click each tab → the matching statement is selected/scrolled in the editor.
3. Put the cursor inside the middle statement, press `Cmd/Ctrl+Enter` → a single tab for that statement.
4. Select part of one statement, press `Cmd/Ctrl+Enter` → that whole statement runs (one tab).
5. Include a deliberately broken statement (e.g. `SELEC 1`) among valid ones and Run-all → its tab shows a red dot + query-error alert while sibling tabs still show grids.
6. Confirm CSV export works from a success tab.

- [ ] **Step 8: Commit**

```bash
git add src/frontend/src/routes/_authed/query/index.tsx
git commit -m "Wire query playground: selection/cursor/all runs with result tabs"
```

---

## Self-Review

**Spec coverage:**
- Client-split, one call per statement → Task 5 (`useQueryRuns`). ✅
- Run resolution (selection∩ / cursor / all; partial→full) → Task 2 + Task 8 keymaps. ✅
- Parallel dispatch capped at 6 → Task 3 + Task 5. ✅
- Tabs with snippet + click-to-highlight → Task 7 + Task 4 + Task 8 `onHighlight`. ✅
- Per-tab blocked/query-error/grid/CSV → Task 6. ✅
- Splitter handling `;`/literals/dollar-quotes/comments/trailing → Task 1. ✅
- Edge cases (empty editor no-op, re-run replaces, ephemeral runs) → Task 8 disabled state + Task 5 batch replace. ✅

**Placeholder scan:** No TBD/TODO; every code step shows full code. ✅

**Type consistency:** `SqlStatement` (Task 1) consumed by Tasks 2/5/8; `RunEntry` (Task 5) consumed by Tasks 6/7/8; `highlightStatementInEditor(view, from, to)` (Task 4) called in Task 8; `run(databaseId, statements)` / `isRunning` / `runs` (Task 5) used in Task 8; `selectStatements(statements, selection, runAll)` (Task 2) used in Task 8. Names match across tasks. ✅

**Out of scope (unchanged):** no backend/engine/OpenAPI edits; no pinned history; no multi-document editor.
