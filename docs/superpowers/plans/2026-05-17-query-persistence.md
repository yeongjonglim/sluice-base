# Query Editor Session Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist the query editor's selected database, SQL text, and expanded sidebar nodes across page refreshes using sessionStorage (per-tab, survives refresh but not browser close).

**Architecture:** A `useSessionState<T>(key, defaultValue)` hook mirrors the `useState` API, lazy-initializing from sessionStorage synchronously (no flash) and writing back via `useEffect` on every change. Four call sites replace existing `useState` calls in `QueryPage` and `SchemaSidebar`. The sidebar's `Set<string>` types are converted to `string[]` for JSON-serializability.

**Tech Stack:** React 19, TypeScript, Vitest, @testing-library/react, jsdom

---

## File Map

| Action | Path |
|---|---|
| Create | `src/frontend/src/utils/useSessionState.ts` |
| Create | `src/frontend/src/utils/__tests__/useSessionState.test.ts` |
| Modify | `src/frontend/src/routes/_authed/query/index.tsx` |
| Modify | `src/frontend/src/routes/_authed/__tests__/query-clear.test.tsx` |

---

## Task 1: `useSessionState` hook

**Files:**
- Create: `src/frontend/src/utils/__tests__/useSessionState.test.ts`
- Create: `src/frontend/src/utils/useSessionState.ts`

- [ ] **Step 1: Write the failing tests**

Create `src/frontend/src/utils/__tests__/useSessionState.test.ts`:

```ts
import { renderHook, act } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { useSessionState } from "@/utils/useSessionState";

beforeEach(() => sessionStorage.clear());

describe("useSessionState", () => {
  it("returns defaultValue when sessionStorage is empty", () => {
    const { result } = renderHook(() => useSessionState("k", "default"));
    expect(result.current[0]).toBe("default");
  });

  it("returns persisted value when key is already in sessionStorage", () => {
    sessionStorage.setItem("k", JSON.stringify("persisted"));
    const { result } = renderHook(() => useSessionState("k", "default"));
    expect(result.current[0]).toBe("persisted");
  });

  it("writes new value to sessionStorage when state changes", () => {
    const { result } = renderHook(() => useSessionState("k", "default"));
    act(() => result.current[1]("updated"));
    expect(JSON.parse(sessionStorage.getItem("k")!)).toBe("updated");
  });

  it("falls back to defaultValue when sessionStorage contains invalid JSON", () => {
    sessionStorage.setItem("k", "{{{not-json");
    const { result } = renderHook(() => useSessionState("k", "default"));
    expect(result.current[0]).toBe("default");
  });

  it("works with null as defaultValue", () => {
    const { result } = renderHook(() => useSessionState<string | null>("k", null));
    expect(result.current[0]).toBeNull();
  });

  it("works with array values", () => {
    const { result } = renderHook(() => useSessionState<string[]>("k", []));
    act(() => result.current[1](["a", "b"]));
    expect(JSON.parse(sessionStorage.getItem("k")!)).toEqual(["a", "b"]);
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd src/frontend && npm test -- --reporter=verbose src/utils/__tests__/useSessionState.test.ts
```

Expected: all 6 tests fail with "Cannot find module '@/utils/useSessionState'".

- [ ] **Step 3: Implement the hook**

Create `src/frontend/src/utils/useSessionState.ts`:

```ts
import { useState, useEffect, type Dispatch, type SetStateAction } from "react";

export function useSessionState<T>(
  key: string,
  defaultValue: T,
): [T, Dispatch<SetStateAction<T>>] {
  const [value, setValue] = useState<T>(() => {
    const stored = sessionStorage.getItem(key);
    if (stored === null) return defaultValue;
    try {
      return JSON.parse(stored) as T;
    } catch {
      return defaultValue;
    }
  });

  useEffect(() => {
    sessionStorage.setItem(key, JSON.stringify(value));
  }, [key, value]);

  return [value, setValue];
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
cd src/frontend && npm test -- --reporter=verbose src/utils/__tests__/useSessionState.test.ts
```

Expected: all 6 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/utils/useSessionState.ts src/frontend/src/utils/__tests__/useSessionState.test.ts
git commit -m "feat: add useSessionState hook for sessionStorage-backed state"
```

---

## Task 2: Wire persistence into `QueryPage` and `SchemaSidebar`

**Files:**
- Modify: `src/frontend/src/routes/_authed/query/index.tsx`
- Modify: `src/frontend/src/routes/_authed/__tests__/query-clear.test.tsx`

- [ ] **Step 1: Verify baseline tests pass before touching anything**

```bash
cd src/frontend && npm test -- --reporter=verbose src/routes/_authed/__tests__/query-clear.test.tsx
```

Expected: all 3 tests pass.

- [ ] **Step 2: Add `sessionStorage.clear()` to the existing test to prevent inter-test pollution**

In `src/frontend/src/routes/_authed/__tests__/query-clear.test.tsx`, add a `beforeEach` call after the existing `afterEach`:

```ts
afterEach(cleanup);
beforeEach(() => sessionStorage.clear());  // ← add this line
```

- [ ] **Step 3: Replace `useState` calls in `QueryPage` with `useSessionState`**

In `src/frontend/src/routes/_authed/query/index.tsx`:

Add the import at the top (alongside other imports):
```ts
import { useSessionState } from "@/utils/useSessionState";
```

In `QueryPage`, replace:
```ts
const [selectedDatabaseId, setSelectedDatabaseId] = useState<string | null>(null);
// ...
const [editorContent, setEditorContent] = useState("");
```

With:
```ts
const [selectedDatabaseId, setSelectedDatabaseId] = useSessionState<string | null>(
  "sluice:query:db",
  null,
);
// ...
const [editorContent, setEditorContent] = useSessionState("sluice:query:editor", "");
```

Remove `useState` from the React import if it's no longer used in `QueryPage` — but `SchemaSidebar` still uses it, so leave the import as-is for now.

- [ ] **Step 4: Replace `Set<string>` state in `SchemaSidebar` with `useSessionState<string[]>`**

In `SchemaSidebar`, replace:
```ts
const [expandedSchemas, setExpandedSchemas] = useState<Set<string>>(new Set());
const [expandedTables, setExpandedTables] = useState<Set<string>>(new Set());
```

With:
```ts
const [expandedSchemas, setExpandedSchemas] = useSessionState<string[]>(
  "sluice:query:expandedSchemas",
  [],
);
const [expandedTables, setExpandedTables] = useSessionState<string[]>(
  "sluice:query:expandedTables",
  [],
);
```

- [ ] **Step 5: Update `toggleSchema` and `toggleTable` to use array operations**

Replace `toggleSchema`:
```ts
function toggleSchema(name: string) {
  setExpandedSchemas((prev) =>
    prev.includes(name) ? prev.filter((s) => s !== name) : [...prev, name],
  );
}
```

Replace `toggleTable`:
```ts
function toggleTable(key: string) {
  setExpandedTables((prev) =>
    prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key],
  );
}
```

- [ ] **Step 6: Update expansion checks from `.has()` to `.includes()`**

Replace:
```ts
const schemaExpanded = expandedSchemas.has(s.name);
```
With:
```ts
const schemaExpanded = expandedSchemas.includes(s.name);
```

Replace:
```ts
const tableExpanded = expandedTables.has(tableKey);
```
With:
```ts
const tableExpanded = expandedTables.includes(tableKey);
```

- [ ] **Step 7: Remove the now-unused `useState` import if applicable**

Check the import at the top of `query/index.tsx`:
```ts
import React, { useCallback, useEffect, useRef, useState } from "react";
```

`SchemaSidebar` no longer uses `useState`, and `QueryPage` no longer uses `useState`. Remove `useState` from the import:
```ts
import React, { useCallback, useEffect, useRef } from "react";
```

- [ ] **Step 8: Run all tests**

```bash
cd src/frontend && npm test
```

Expected: all tests pass (6 from useSessionState + 3 from query-clear).

- [ ] **Step 9: Commit**

```bash
git add src/frontend/src/routes/_authed/query/index.tsx \
        src/frontend/src/routes/_authed/__tests__/query-clear.test.tsx
git commit -m "feat: persist query editor state across page refreshes via sessionStorage"
```
