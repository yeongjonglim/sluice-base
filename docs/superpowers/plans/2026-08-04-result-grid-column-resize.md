# Result Grid Column Resize Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `ResultTable`'s char-count column-width estimate with real proportional-font measurement, and let users drag or double-click to resize columns.

**Architecture:** A generic `measureText` canvas helper (with a char-count fallback for jsdom) backs a pure `columnWidths`/`autoFitWidth` sizing module (#172). A `useColumnWidths` hook holds the current widths in state, resets them when the result's columns change, and exposes pointer-drag and double-click auto-fit handlers (#173). `ResultTable` renders a small resize handle in each header cell.

**Tech Stack:** React 19, TypeScript, Mantine `Table`, TanStack Virtual, Vitest + Testing Library (jsdom).

## Global Constraints

- Work on branch `feat/result-grid-column-resize` (already created). Never commit to `main`.
- Commit messages are a single subject line — no body paragraph.
- TypeScript: use `Array<T>`, never `T[]` (ESLint `@typescript-eslint/array-type`).
- Preserve existing comments; only move or reword one if it would otherwise be factually wrong.
- Gate every task by running, from `src/frontend/`: `npm run lint` and `npm run test`. Both must pass before committing.
- All shell commands below run from `src/frontend/`.

---

### Task 1: `measureText` canvas helper

**Files:**
- Create: `src/frontend/src/utils/measureText.ts`
- Test: `src/frontend/src/utils/__tests__/measureText.test.ts`

**Interfaces:**
- Consumes: nothing.
- Produces: `export function measureText(text: string, font: string): number` — pixel width of `text` rendered in `font` (a CSS `font` shorthand, e.g. `"12px sans-serif"`). Falls back to `text.length * 6.6` when no 2D canvas context is available.

- [ ] **Step 1: Write the failing test**

```ts
// src/frontend/src/utils/__tests__/measureText.test.ts
import { afterEach, describe, expect, it, vi } from "vitest";
import { measureText } from "@/utils/measureText";

afterEach(() => vi.restoreAllMocks());

describe("measureText", () => {
  it("falls back to a per-character estimate when no 2D context is available", () => {
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(null);
    expect(measureText("hello", "12px sans-serif")).toBeCloseTo(5 * 6.6);
  });

  it("uses the canvas 2D context when one is available", () => {
    const ctx = { font: "", measureText: (t: string) => ({ width: t.length * 10 }) };
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(
      ctx as unknown as CanvasRenderingContext2D,
    );
    expect(measureText("hi", "10px sans-serif")).toBe(20);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/utils/__tests__/measureText.test.ts`
Expected: FAIL — cannot resolve `@/utils/measureText` (module does not exist yet).

- [ ] **Step 3: Write minimal implementation**

```ts
// src/frontend/src/utils/measureText.ts

// Measures rendered text width for the column-width estimator. A single canvas is
// reused across calls; getContext() is invoked each call (it returns the same
// cached context) so tests can stub it. When no 2D context exists — jsdom without
// the `canvas` package — we fall back to a flat per-character estimate.
const FALLBACK_CHAR_PX = 6.6;

let canvas: HTMLCanvasElement | null = null;

function context(): CanvasRenderingContext2D | null {
  try {
    canvas ??= document.createElement("canvas");
    return canvas.getContext("2d");
  } catch {
    return null;
  }
}

export function measureText(text: string, font: string): number {
  const ctx = context();
  if (!ctx) return text.length * FALLBACK_CHAR_PX;
  ctx.font = font;
  return ctx.measureText(text).width;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `npx vitest run src/utils/__tests__/measureText.test.ts`
Expected: PASS (2 tests).

- [ ] **Step 5: Lint and commit**

```bash
npm run lint
git add src/utils/measureText.ts src/utils/__tests__/measureText.test.ts
git commit -m "Add measureText canvas helper with char-count fallback"
```

---

### Task 2: Measurement-based `columnWidths` + `autoFitWidth` (#172)

**Files:**
- Create: `src/frontend/src/components/query/columnWidths.ts`
- Test: `src/frontend/src/components/query/__tests__/columnWidths.test.ts`
- Modify: `src/frontend/src/components/query/ResultTable.tsx` (remove the inline `columnWidths` function and its width constants; import from the new module)

**Interfaces:**
- Consumes: `measureText(text, font)` from Task 1.
- Produces:
  - `export const MANUAL_MAX_COL_PX = 800`
  - `export function columnWidths(columns: Array<string>, rows: Array<Array<string | null>>): Array<number>` — clamped to `[MIN_COL_PX, MAX_COL_PX]`.
  - `export function autoFitWidth(columns: Array<string>, rows: Array<Array<string | null>>, index: number): number` — clamped to `[MIN_COL_PX, MANUAL_MAX_COL_PX]` (no 360 cap).

- [ ] **Step 1: Write the failing test**

```ts
// src/frontend/src/components/query/__tests__/columnWidths.test.ts
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { autoFitWidth, columnWidths } from "@/components/query/columnWidths";

// Force measureText's char-count fallback (6.6px/char) so widths are deterministic.
beforeEach(() => {
  vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(null);
});
afterEach(() => vi.restoreAllMocks());

describe("columnWidths", () => {
  it("clamps a short column up to the minimum width", () => {
    // "id" -> ceil(2*6.6)=14, +24 chrome = 38, clamped up to MIN 56.
    expect(columnWidths(["id"], [["1"]])).toEqual([56]);
  });

  it("clamps a very wide column down to the maximum width", () => {
    // 80 chars -> ceil(528)+24=552, clamped down to MAX 360.
    expect(columnWidths(["c"], [["x".repeat(80)]])).toEqual([360]);
  });

  it("sizes to the header when it is wider than the cells", () => {
    // 23-char header -> ceil(151.8)=152, +24 = 176; wider than the "x" cell.
    expect(columnWidths(["a_very_long_header_name"], [["x"]])).toEqual([176]);
  });

  it("treats null cells as the text 'NULL' without throwing", () => {
    expect(columnWidths(["c"], [[null]])).toEqual([56]);
  });
});

describe("autoFitWidth", () => {
  it("fits past the 360 estimate cap, up to the manual maximum", () => {
    // Same 552px content: columnWidths clamps to 360, autoFit keeps 552.
    expect(columnWidths(["c"], [["x".repeat(80)]])).toEqual([360]);
    expect(autoFitWidth(["c"], [["x".repeat(80)]], 0)).toBe(552);
  });

  it("clamps to the manual maximum of 800", () => {
    // 200 chars -> ceil(1320)+24 = 1344, clamped to 800.
    expect(autoFitWidth(["c"], [["x".repeat(200)]], 0)).toBe(800);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/components/query/__tests__/columnWidths.test.ts`
Expected: FAIL — cannot resolve `@/components/query/columnWidths`.

- [ ] **Step 3: Create the module**

```ts
// src/frontend/src/components/query/columnWidths.ts
import { measureText } from "@/utils/measureText";

// The initial column widths are estimated once from the header + a sample of rows,
// which keeps the layout stable while rows are virtualized — with content-based
// sizing the columns would jump as different rows scroll into view. These widths are
// the starting defaults; users can drag or double-click to resize (see useColumnWidths).
const CELL_CHROME_PX = 24; // padding + border allowance
const MIN_COL_PX = 56;
const MAX_COL_PX = 360; // cap for the auto-estimate only
const SAMPLE_ROWS = 200;

// Manual drags and double-click auto-fit may exceed the estimate cap, up to this.
export const MANUAL_MAX_COL_PX = 800;

function clamp(value: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, value));
}

// Header cells render semibold; data cells use the base xs font. Font strings are
// built from Mantine's CSS variables so measurement matches what the table renders.
function cellFonts(): { header: string; body: string } {
  const root = getComputedStyle(document.documentElement);
  const family =
    root.getPropertyValue("--mantine-font-family").trim() ||
    "-apple-system, BlinkMacSystemFont, Segoe UI, Roboto, sans-serif";
  const size = root.getPropertyValue("--mantine-font-size-xs").trim() || "12px";
  return { header: `600 ${size} ${family}`, body: `${size} ${family}` };
}

// Widest rendered content in a column (header vs. sampled cells), plus cell chrome.
function contentWidth(
  columns: Array<string>,
  rows: Array<Array<string | null>>,
  index: number,
  fonts: { header: string; body: string },
): number {
  const sample = rows.length > SAMPLE_ROWS ? rows.slice(0, SAMPLE_ROWS) : rows;
  let max = measureText(columns[index], fonts.header);
  for (const row of sample) {
    const value = row[index];
    const width = measureText(value === null ? "NULL" : value, fonts.body);
    if (width > max) max = width;
  }
  return Math.ceil(max) + CELL_CHROME_PX;
}

export function columnWidths(
  columns: Array<string>,
  rows: Array<Array<string | null>>,
): Array<number> {
  const fonts = cellFonts();
  return columns.map((_, index) =>
    Math.round(clamp(contentWidth(columns, rows, index, fonts), MIN_COL_PX, MAX_COL_PX)),
  );
}

export function autoFitWidth(
  columns: Array<string>,
  rows: Array<Array<string | null>>,
  index: number,
): number {
  const fonts = cellFonts();
  return Math.round(
    clamp(contentWidth(columns, rows, index, fonts), MIN_COL_PX, MANUAL_MAX_COL_PX),
  );
}
```

- [ ] **Step 4: Run the new test to verify it passes**

Run: `npx vitest run src/components/query/__tests__/columnWidths.test.ts`
Expected: PASS (6 tests).

- [ ] **Step 5: Remove the inline copy from `ResultTable.tsx`**

In `src/frontend/src/components/query/ResultTable.tsx`:

Delete the width constants and the local `columnWidths` function (the block spanning the `CHAR_PX` / `CELL_CHROME_PX` / `MIN_COL_PX` / `MAX_COL_PX` / `SAMPLE_ROWS` constants, the "Fixed column widths…" comment, and the `function columnWidths(...) { ... }` definition). Keep `ROW_HEIGHT` and the `ELLIPSIS` constant.

Add the import near the other local imports:

```ts
import { columnWidths } from "@/components/query/columnWidths";
```

Leave the existing `const widths = useMemo(() => columnWidths(columns, rows), [columns, rows]);` and `totalWidth` lines unchanged — they now call the imported function.

- [ ] **Step 6: Verify the whole suite still passes**

Run: `npm run test`
Expected: PASS — existing `ResultTable` tests (headers, filter, row count) still green; new `columnWidths` tests pass.

- [ ] **Step 7: Lint and commit**

```bash
npm run lint
git add src/components/query/columnWidths.ts \
        src/components/query/__tests__/columnWidths.test.ts \
        src/components/query/ResultTable.tsx
git commit -m "Estimate result-grid column widths from measured text (#172)"
```

---

### Task 3: `useColumnWidths` hook + draggable resize handles (#173)

**Files:**
- Create: `src/frontend/src/components/query/useColumnWidths.ts`
- Modify: `src/frontend/src/components/query/ResultTable.tsx` (use the hook; render a resize handle per header cell)
- Test: `src/frontend/src/components/query/__tests__/ResultTable.test.tsx` (add a resize/auto-fit/reset describe block)

**Interfaces:**
- Consumes: `columnWidths`, `autoFitWidth`, `MANUAL_MAX_COL_PX` from Task 2.
- Produces: `export function useColumnWidths(columns: Array<string>, rows: Array<Array<string | null>>): { widths: Array<number>; totalWidth: number; onResizeStart: (index: number, event: React.PointerEvent) => void; onAutoFit: (index: number) => void }`.

- [ ] **Step 1: Write the failing test**

Append to `src/frontend/src/components/query/__tests__/ResultTable.test.tsx`. Add `beforeEach`, `afterEach`, and `vi` to the existing `vitest` import, and `fireEvent` to the existing `@testing-library/react` import.

```tsx
describe("ResultTable column resizing", () => {
  // Force the char-count fallback (6.6px/char) so widths are deterministic.
  beforeEach(() => {
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(null);
  });
  afterEach(() => vi.restoreAllMocks());

  function renderCustom(columns: Array<string>, rows: Array<Array<string | null>>) {
    return render(
      <MantineProvider>
        <ResultTable
          columns={columns}
          rows={rows}
          rowCount={rows.length}
          durationMs={1}
          resultIndex={0}
        />
      </MantineProvider>,
    );
  }

  function colWidths(container: HTMLElement): Array<string> {
    return Array.from(container.querySelectorAll("col")).map(
      (col) => (col as HTMLElement).style.width,
    );
  }

  it("widens a column when its handle is dragged right", () => {
    const { container } = renderCustom(["id", "name"], [["1", "Ada"]]);
    expect(colWidths(container)[0]).toBe("56px");

    const handle = screen.getByLabelText("Resize id column");
    fireEvent.pointerDown(handle, { clientX: 0 });
    fireEvent.pointerMove(window, { clientX: 100 });
    fireEvent.pointerUp(window, { clientX: 100 });

    // 56 + 100 = 156, within [56, 800].
    expect(colWidths(container)[0]).toBe("156px");
  });

  it("auto-fits a column to its content on double-click", () => {
    const long = "x".repeat(80);
    const { container } = renderCustom(["id", "name"], [["1", long]]);
    // Estimate clamps the 552px content down to the 360 cap.
    expect(colWidths(container)[1]).toBe("360px");

    fireEvent.dblClick(screen.getByLabelText("Resize name column"));
    // Auto-fit ignores the 360 cap and fits the real content.
    expect(colWidths(container)[1]).toBe("552px");
  });

  it("resets widths when the result's columns change", () => {
    const { container, rerender } = renderCustom(["id", "name"], [["1", "Ada"]]);
    fireEvent.pointerDown(screen.getByLabelText("Resize id column"), { clientX: 0 });
    fireEvent.pointerMove(window, { clientX: 100 });
    fireEvent.pointerUp(window, { clientX: 100 });
    expect(colWidths(container)[0]).toBe("156px");

    rerender(
      <MantineProvider>
        <ResultTable columns={["v"]} rows={[["1"]]} rowCount={1} durationMs={1} resultIndex={0} />
      </MantineProvider>,
    );
    // Fresh estimate for the new single column; the dragged 156px is gone.
    expect(colWidths(container)).toEqual(["56px"]);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/components/query/__tests__/ResultTable.test.tsx`
Expected: FAIL — no element with label `Resize id column` (handles not rendered yet).

- [ ] **Step 3: Create the hook**

```ts
// src/frontend/src/components/query/useColumnWidths.ts
import { useEffect, useMemo, useRef, useState } from "react";
import type { PointerEvent as ReactPointerEvent } from "react";
import { MANUAL_MAX_COL_PX, autoFitWidth, columnWidths } from "@/components/query/columnWidths";

const MIN_COL_PX = 56; // resize floor; matches the columnWidths clamp minimum

function clamp(value: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, value));
}

export function useColumnWidths(
  columns: Array<string>,
  rows: Array<Array<string | null>>,
) {
  const estimated = useMemo(() => columnWidths(columns, rows), [columns, rows]);

  // Widths are local to the current result. When the column set changes (a new
  // query), reset to a fresh estimate — the React-endorsed "adjust state during
  // render on prop change" pattern, keyed on the column-name signature. NUL is
  // used as the join separator so ordinary names can't collide.
  const signature = columns.join("\u0000");
  const [state, setState] = useState({ signature, widths: estimated });
  if (state.signature !== signature) {
    setState({ signature, widths: estimated });
  }
  const widths = state.signature === signature ? state.widths : estimated;
  const totalWidth = widths.reduce((a, b) => a + b, 0);

  // Listeners for the in-flight drag, so we can detach them if the grid unmounts
  // mid-drag. Only one column resizes at a time.
  const drag = useRef<{ move: (e: PointerEvent) => void; up: () => void } | null>(null);

  useEffect(
    () => () => {
      if (drag.current) {
        window.removeEventListener("pointermove", drag.current.move);
        window.removeEventListener("pointerup", drag.current.up);
        document.body.style.userSelect = "";
      }
    },
    [],
  );

  function setWidth(index: number, width: number) {
    setState((s) => {
      const next = s.widths.slice();
      next[index] = width;
      return { signature: s.signature, widths: next };
    });
  }

  function onResizeStart(index: number, event: ReactPointerEvent) {
    event.preventDefault();
    const startX = event.clientX;
    const startWidth = widths[index];
    document.body.style.userSelect = "none";

    const move = (e: PointerEvent) => {
      setWidth(index, Math.round(clamp(startWidth + (e.clientX - startX), MIN_COL_PX, MANUAL_MAX_COL_PX)));
    };
    const up = () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
      document.body.style.userSelect = "";
      drag.current = null;
    };

    drag.current = { move, up };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
  }

  function onAutoFit(index: number) {
    setWidth(index, autoFitWidth(columns, rows, index));
  }

  return { widths, totalWidth, onResizeStart, onAutoFit };
}
```

- [ ] **Step 4: Wire the hook and handles into `ResultTable.tsx`**

Add imports:

```ts
import type { PointerEvent as ReactPointerEvent } from "react";
import { useColumnWidths } from "@/components/query/useColumnWidths";
```

Remove the now-unused `columnWidths` import added in Task 2 and the two `useMemo` lines for `widths` / `totalWidth`, replacing them with the hook call (place it near the other hooks, after the `filtered` memo):

```ts
const { widths, totalWidth, onResizeStart, onAutoFit } = useColumnWidths(columns, rows);
```

Add the handle component below the `ELLIPSIS` constant:

```tsx
function ColumnResizeHandle({
  label,
  onPointerDown,
  onDoubleClick,
}: {
  label: string;
  onPointerDown: (event: ReactPointerEvent) => void;
  onDoubleClick: () => void;
}) {
  return (
    <div
      role="separator"
      aria-orientation="vertical"
      aria-label={`Resize ${label} column`}
      onPointerDown={onPointerDown}
      onDoubleClick={onDoubleClick}
      style={{
        position: "absolute",
        top: 0,
        right: 0,
        height: "100%",
        width: 6,
        cursor: "col-resize",
        touchAction: "none",
        userSelect: "none",
      }}
    />
  );
}
```

Update the header cells to carry an index, become a positioning context, and render the handle:

```tsx
{columns.map((col, i) => (
  <Table.Th key={col} style={{ ...ELLIPSIS, position: "relative" }}>
    {col}
    <ColumnResizeHandle
      label={col}
      onPointerDown={(e) => onResizeStart(i, e)}
      onDoubleClick={() => onAutoFit(i)}
    />
  </Table.Th>
))}
```

Leave the `<colgroup>`/`<col>` block and the `style={{ tableLayout: "fixed", width: totalWidth }}` unchanged — they read the hook's `widths`/`totalWidth`.

- [ ] **Step 5: Run the new tests to verify they pass**

Run: `npx vitest run src/components/query/__tests__/ResultTable.test.tsx`
Expected: PASS — resize, auto-fit, reset, plus the original header/filter/count tests.

- [ ] **Step 6: Run the full suite, lint, and type-check**

Run: `npm run test` then `npm run lint`
Expected: PASS on both. (Lint covers `Array<T>` usage and the react-hooks rules.)

- [ ] **Step 7: Commit**

```bash
git add src/components/query/useColumnWidths.ts src/components/query/ResultTable.tsx \
        src/components/query/__tests__/ResultTable.test.tsx
git commit -m "Add draggable column resize and double-click auto-fit to result grid (#173)"
```

---

## Self-Review

**Spec coverage:**
- #172 measured initial estimate → Task 2 (`columnWidths` on `measureText`, CSS-var fonts, header-vs-cell max, same clamp/sample). ✓
- jsdom char-count fallback → Task 1 (`measureText` fallback) + forced in Tasks 2/3 tests. ✓
- #173 drag resize → Task 3 (`onResizeStart`, window listeners, `[MIN, 800]` clamp). ✓
- #173 double-click auto-fit over sampled rows, past the 360 cap → Task 3 `onAutoFit` + Task 2 `autoFitWidth`. ✓
- Local-to-result state, reset on column change → Task 3 signature reset. ✓
- File extraction (`measureText.ts`, `columnWidths.ts`, `useColumnWidths.ts`, handle) → Tasks 1–3. ✓
- Testing (measureText fallback, columnWidths clamps, resize/auto-fit/reset) → Tasks 1–3. ✓

**Placeholder scan:** No TBD/TODO; every code and test step is concrete. ✓

**Type consistency:** `measureText(text, font)`, `columnWidths(columns, rows)`, `autoFitWidth(columns, rows, index)`, and `useColumnWidths(columns, rows)` return names (`widths`, `totalWidth`, `onResizeStart`, `onAutoFit`) match across the module boundary, the hook, and the `ColumnResizeHandle` props. `MIN_COL_PX = 56` and `MANUAL_MAX_COL_PX = 800` are consistent between `columnWidths.ts` and `useColumnWidths.ts`. ✓
