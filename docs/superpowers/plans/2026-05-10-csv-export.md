# CSV Export for Query Results Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a client-side "Export CSV" button to the query results view that downloads the currently-shown rows as a CSV file without any additional network requests.

**Architecture:** Two exported helper functions are added to `query.tsx`: `buildCsv` (pure string builder, RFC 4180 compliant) and `exportToCsv` (DOM wrapper that triggers the browser download). The `QueryResults` success branch gains a `Group` header row with the existing row-count text on the left and an Export CSV button on the right.

**Tech Stack:** React, Mantine v9, @tabler/icons-react, Vitest + jsdom

---

### Task 1: Add `buildCsv` and `exportToCsv` helpers with tests

**Files:**
- Modify: `src/frontend/src/routes/_authed/query.tsx`
- Create: `src/frontend/src/routes/_authed/__tests__/export-csv.test.ts`

- [ ] **Step 1: Create the failing test file**

Create `src/frontend/src/routes/_authed/__tests__/export-csv.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { buildCsv, exportToCsv } from "../query";

describe("buildCsv", () => {
  it("produces a header row and data rows", () => {
    const result = buildCsv(["id", "name"], [["1", "Alice"], ["2", "Bob"]]);
    expect(result).toBe("id,name\n1,Alice\n2,Bob");
  });

  it("renders null and undefined cells as empty strings", () => {
    const result = buildCsv(["a", "b"], [[null, undefined]]);
    expect(result).toBe("a,b\n,");
  });

  it("wraps values containing commas in double-quotes", () => {
    const result = buildCsv(["v"], [["hello, world"]]);
    expect(result).toBe('v\n"hello, world"');
  });

  it("escapes double-quotes inside quoted values by doubling them", () => {
    const result = buildCsv(["v"], [['say "hi"']]);
    expect(result).toBe('v\n"say ""hi"""');
  });

  it("wraps values containing newlines in double-quotes", () => {
    const result = buildCsv(["v"], [["line1\nline2"]]);
    expect(result).toBe('v\n"line1\nline2"');
  });

  it("produces only the header row when rows array is empty", () => {
    const result = buildCsv(["col1", "col2"], []);
    expect(result).toBe("col1,col2");
  });
});

describe("exportToCsv", () => {
  const mockAnchor = {
    href: "",
    download: "",
    click: vi.fn(),
  };

  beforeEach(() => {
    vi.stubGlobal("URL", {
      createObjectURL: vi.fn(() => "blob:fake-url"),
      revokeObjectURL: vi.fn(),
    });
    vi.spyOn(document, "createElement").mockImplementation((tag: string) => {
      if (tag === "a") return mockAnchor as unknown as HTMLElement;
      return document.createElement(tag);
    });
    mockAnchor.href = "";
    mockAnchor.download = "";
    mockAnchor.click.mockClear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("sets href, download, and calls click", () => {
    exportToCsv(["id"], [["1"]], "results.csv");
    expect(mockAnchor.href).toBe("blob:fake-url");
    expect(mockAnchor.download).toBe("results.csv");
    expect(mockAnchor.click).toHaveBeenCalledOnce();
  });

  it("revokes the object URL after clicking", () => {
    exportToCsv(["id"], [["1"]], "results.csv");
    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:fake-url");
  });
});
```

- [ ] **Step 2: Run the tests and confirm they fail**

```bash
cd src/frontend && npx vitest run src/routes/_authed/__tests__/export-csv.test.ts
```

Expected: FAIL — `buildCsv` and `exportToCsv` are not yet exported from `query.tsx`.

- [ ] **Step 3: Add `buildCsv` and `exportToCsv` to `query.tsx`**

Add these two functions directly after the import block and before the `Route` export at the top of `src/frontend/src/routes/_authed/query.tsx`:

```ts
export function buildCsv(
  columns: string[],
  rows: (string | null | undefined)[][],
): string {
  const escape = (val: string | null | undefined): string => {
    const s = val == null ? "" : String(val);
    if (s.includes(",") || s.includes('"') || s.includes("\n")) {
      return `"${s.replace(/"/g, '""')}"`;
    }
    return s;
  };
  const lines = [
    columns.map(escape).join(","),
    ...rows.map((row) => row.map(escape).join(",")),
  ];
  return lines.join("\n");
}

export function exportToCsv(
  columns: string[],
  rows: (string | null | undefined)[][],
  filename: string,
): void {
  const csv = buildCsv(columns, rows);
  const blob = new Blob([csv], { type: "text/csv" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

```bash
cd src/frontend && npx vitest run src/routes/_authed/__tests__/export-csv.test.ts
```

Expected: all 8 tests PASS.

- [ ] **Step 5: Run the full test suite to check for regressions**

```bash
cd src/frontend && npm test
```

Expected: all tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/routes/_authed/query.tsx src/frontend/src/routes/_authed/__tests__/export-csv.test.ts
git commit -m "feat: add buildCsv and exportToCsv helpers with RFC 4180 escaping"
```

---

### Task 2: Add Export CSV button to `QueryResults`

**Files:**
- Modify: `src/frontend/src/routes/_authed/query.tsx`

- [ ] **Step 1: Add `IconDownload` to the tabler icons import**

In `src/frontend/src/routes/_authed/query.tsx`, update the `@tabler/icons-react` import line to include `IconDownload`:

```ts
import {
  IconChevronDown,
  IconChevronRight,
  IconDatabase,
  IconDownload,
  IconPlayerPlay,
  IconPlaylistAdd,
  IconTable,
} from "@tabler/icons-react";
```

- [ ] **Step 2: Replace the plain `Text` row with a `Group` header in `QueryResults`**

In the success return of `QueryResults` (the last `return` in that function), replace:

```tsx
    <Stack mt="md" gap="xs">
      <Text size="xs" c="dimmed">
        {result.rowCount} {result.rowCount === 1 ? "row" : "rows"} · {result.durationMs} ms
      </Text>
```

with:

```tsx
    <Stack mt="md" gap="xs">
      <Group justify="space-between" align="center">
        <Text size="xs" c="dimmed">
          {result.rowCount} {result.rowCount === 1 ? "row" : "rows"} · {result.durationMs} ms
        </Text>
        <Button
          size="xs"
          variant="subtle"
          leftSection={<IconDownload size={14} />}
          onClick={() =>
            exportToCsv(columns, rows, `query-results-${Date.now()}.csv`)
          }
        >
          Export CSV
        </Button>
      </Group>
```

- [ ] **Step 3: Run the full test suite to check for regressions**

```bash
cd src/frontend && npm test
```

Expected: all tests PASS (no type errors, no test failures).

- [ ] **Step 4: Commit**

```bash
git add src/frontend/src/routes/_authed/query.tsx
git commit -m "feat: add Export CSV button to query results header"
```
