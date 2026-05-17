# Recreate Update Request Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Recreate" button to the Update Request detail page that navigates to the new-request form pre-filled with the original database, SQL, and reason.

**Architecture:** Pure frontend change. The detail page (`/update/$id`) gains a "Recreate" button that navigates to `/update/new?from=<id>`. The new-request form reads the `from` search param, fetches the original request (React Query cache hit in the normal flow), and seeds its three form fields via `useEffect` + a ref guard that fires only once.

**Tech Stack:** React, TanStack Router, TanStack Query, Mantine, Vitest + React Testing Library

---

## File Map

| File | Change |
|------|--------|
| `src/frontend/src/routes/_authed/update/new.tsx` | Add `validateSearch`, `useQuery` with `enabled`, `useEffect` seed, export component |
| `src/frontend/src/routes/_authed/update/$id.tsx` | Add `useNavigate`, "Recreate" button in action group |
| `src/frontend/src/routes/_authed/__tests__/new-update-prefill.test.tsx` | New — tests `from` param pre-fill behaviour |
| `src/frontend/src/routes/_authed/__tests__/update-detail-recreate.test.tsx` | New — tests Recreate button visibility |

---

## Task 1: Pre-fill `/update/new` from `?from=<id>`

**Files:**
- Modify: `src/frontend/src/routes/_authed/update/new.tsx`
- Create: `src/frontend/src/routes/_authed/__tests__/new-update-prefill.test.tsx`

### Step 1.1 — Write failing test

Create `src/frontend/src/routes/_authed/__tests__/new-update-prefill.test.tsx`:

```tsx
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { NewUpdatePage } from "@/routes/_authed/update/new.tsx";

let mockFrom: string | undefined = undefined;

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
  execError: "column \"active\" does not exist",
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

// Route.useSearch must return { from } matching the current mockFrom.
vi.mock("@/routes/_authed/update/new.tsx", async (importOriginal) => {
  const mod = await importOriginal<typeof import("@/routes/_authed/update/new.tsx")>();
  return mod;
});

// Patch Route.useSearch after module load — done via the mock below.
// Instead we expose Route and patch it per-test using the module's exported Route.
// Simpler: override useSearch on the Route object before rendering.

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
    mockFrom = "req-1";
    render(React.createElement(NewUpdatePage, { searchFrom: mockFrom }), {
      wrapper: Wrapper,
    });
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
    render(React.createElement(NewUpdatePage, { searchFrom: undefined }), {
      wrapper: Wrapper,
    });
    expect((screen.getByTestId("sql-editor") as HTMLTextAreaElement).value).toBe("");
    expect(
      (screen.getByPlaceholderText(/https:\/\/linear\.app/i) as HTMLTextAreaElement).value,
    ).toBe("");
  });
});
```

- [ ] **Step 1.2 — Run test to verify it fails**

```bash
cd src/frontend && npx vitest run src/routes/_authed/__tests__/new-update-prefill.test.tsx
```

Expected: FAIL — `NewUpdatePage` doesn't accept `searchFrom` prop and has no pre-fill logic.

- [ ] **Step 1.3 — Implement the pre-fill in `new.tsx`**

Replace the entire contents of `src/frontend/src/routes/_authed/update/new.tsx` with:

```tsx
import {
  Box,
  Button,
  Group,
  Select,
  Stack,
  Text,
  Textarea,
  Title,
  useComputedColorScheme,
} from "@mantine/core";
import { createFileRoute, redirect, useNavigate } from "@tanstack/react-router";
import { useEffect, useRef, useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { sql } from "@codemirror/lang-sql";
import { githubDark, githubLight } from "@uiw/codemirror-themes-all";
import { meQueryOptions, useCatalogServer, useSubmitUpdate, useUpdateRequest } from "@/api/hooks";

export const Route = createFileRoute("/_authed/update/new")({
  validateSearch: (search: Record<string, unknown>) => ({
    from: typeof search.from === "string" ? search.from : undefined,
  }),
  beforeLoad: ({ context }) => {
    const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
    if (!me?.permissions.includes("update:submit")) {
      throw redirect({ to: "/" });
    }
  },
  component: NewUpdatePage,
});

export function NewUpdatePage({ searchFrom }: { searchFrom?: string } = {}) {
  // In production, read from the router. In tests, accept as a prop for easy injection.
  const routeSearch = (() => {
    try {
      return Route.useSearch();
    } catch {
      return { from: searchFrom };
    }
  })();
  const from = routeSearch?.from ?? searchFrom;

  const navigate = useNavigate();
  const servers = useCatalogServer();
  const submit = useSubmitUpdate();
  const computedColorScheme = useComputedColorScheme();

  const [databaseId, setDatabaseId] = useState<string | null>(null);
  const [sqlText, setSqlText] = useState("");
  const [reason, setReason] = useState("");

  const source = useUpdateRequest(from ?? "");
  const seeded = useRef(false);

  useEffect(() => {
    if (seeded.current || !source.data) return;
    seeded.current = true;
    setDatabaseId(source.data.databaseId ?? null);
    setSqlText(source.data.sqlText);
    setReason(source.data.reason);
  }, [source.data]);

  const databaseOptions = (servers.data?.servers ?? []).flatMap((s) =>
    s.databases
      .filter((d) => d.canWrite)
      .map((d) => ({ value: d.id, label: `${s.name} — ${d.displayName}` })),
  );

  const canSubmit = databaseId !== null && sqlText.trim() !== "" && reason.trim() !== "";

  function handleSubmit() {
    if (!canSubmit) return;
    submit.mutate(
      { databaseId, sqlText, reason },
      {
        onSuccess: (data) => {
          void navigate({ to: "/update/$id", params: { id: data.id } });
        },
      },
    );
  }

  return (
    <Stack gap="md">
      <Title order={2}>New Update Request</Title>

      <Select
        label="Database"
        placeholder="Select a writable database"
        data={databaseOptions}
        value={databaseId}
        onChange={setDatabaseId}
        required
      />

      <Box>
        <Text size="sm" fw={500} mb={4}>
          SQL{" "}
          <Text span c="red">
            *
          </Text>
        </Text>
        <Box
          style={{
            border: "1px solid var(--mantine-color-default-border)",
            borderRadius: "var(--mantine-radius-sm)",
            overflow: "hidden",
          }}
        >
          <CodeMirror
            value={sqlText}
            onChange={setSqlText}
            extensions={[sql()]}
            theme={computedColorScheme === "dark" ? githubDark : githubLight}
            minHeight="300px"
            basicSetup={{
              lineNumbers: true,
              foldGutter: false,
              defaultKeymap: false,
            }}
          />
        </Box>
      </Box>

      <Textarea
        label="Reason"
        description="Describe why this change is needed. A ticket link is fine."
        placeholder="e.g. https://example.com/ticket/... — fixing bad email for user X"
        required
        minRows={3}
        value={reason}
        onChange={(e) => setReason(e.currentTarget.value)}
      />

      <Group>
        <Button onClick={handleSubmit} loading={submit.isPending} disabled={!canSubmit}>
          Submit for Approval
        </Button>
        <Button variant="subtle" component="a" href="/update">
          Cancel
        </Button>
      </Group>
    </Stack>
  );
}
```

> **Note on `Route.useSearch()` guard:** The `try/catch` lets the component work in tests where the TanStack Router context is mocked away. In production the router context is always present so `Route.useSearch()` never throws. The `searchFrom` prop is only used by tests.

- [ ] **Step 1.4 — Fix `useUpdateRequest` to support `enabled: false`**

The existing hook always fires. When `from` is `undefined` we pass `""` as the id, which would make a bad network call. Add an `enabled` option:

In `src/frontend/src/api/hooks.ts`, locate `useUpdateRequest` (around line 389) and replace it:

```ts
export function useUpdateRequest(id: string) {
  return useQuery({
    queryKey: ["update", id] as const,
    queryFn: () => apiRequest<void, UpdateRequestDetail>(`/api/update/${id}`),
    enabled: id !== "",
  });
}
```

- [ ] **Step 1.5 — Run test to verify it passes**

```bash
cd src/frontend && npx vitest run src/routes/_authed/__tests__/new-update-prefill.test.tsx
```

Expected: PASS (2 tests).

- [ ] **Step 1.6 — Run full test suite to check for regressions**

```bash
cd src/frontend && npm test
```

Expected: all tests pass.

- [ ] **Step 1.7 — Commit**

```bash
git add src/frontend/src/routes/_authed/update/new.tsx \
        src/frontend/src/api/hooks.ts \
        src/frontend/src/routes/_authed/__tests__/new-update-prefill.test.tsx
git commit -m "feat: pre-fill new update request form from ?from=<id>"
```

---

## Task 2: Add "Recreate" button to the detail page

**Files:**
- Modify: `src/frontend/src/routes/_authed/update/$id.tsx`
- Create: `src/frontend/src/routes/_authed/__tests__/update-detail-recreate.test.tsx`

- [ ] **Step 2.1 — Write failing test**

Create `src/frontend/src/routes/_authed/__tests__/update-detail-recreate.test.tsx`:

```tsx
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { MantineProvider } from "@mantine/core";
import React from "react";
import { UpdateDetailPage } from "@/routes/_authed/update/$id.tsx";

const mockNavigate = vi.fn();

vi.mock("@tanstack/react-router", () => ({
  createFileRoute: () => (opts: unknown) => opts,
  redirect: vi.fn(),
  useNavigate: () => mockNavigate,
}));

vi.mock("@uiw/react-codemirror", () => ({
  default: ({ value }: { value: string }) =>
    React.createElement("textarea", { "data-testid": "sql-editor", value, readOnly: true }),
}));

vi.mock("@codemirror/lang-sql", () => ({ sql: () => [] }));
vi.mock("@uiw/codemirror-themes-all", () => ({ githubDark: {}, githubLight: {} }));
vi.mock("@mantine/modals", () => ({ modals: { openConfirmModal: vi.fn() } }));

const fakeDetail = {
  id: "req-1",
  databaseId: "db-abc",
  databaseDisplayName: "Prod — users",
  submitterId: "user-1",
  submitterName: "Alice",
  sqlText: "UPDATE public.users SET active = false WHERE id = 42",
  reason: "Deactivating stale account",
  status: "Executed",
  reviewerId: "user-2",
  reviewerName: "Bob",
  reviewNote: "Approved",
  cancelledById: null,
  cancelledByName: null,
  cancelNote: null,
  executorId: "user-2",
  executorName: "Bob",
  submittedAt: "2026-05-17T00:00:00Z",
  reviewedAt: "2026-05-17T00:30:00Z",
  executedAt: "2026-05-17T01:00:00Z",
  cancelledAt: null,
  execSuccess: false,
  execDurationMs: 120,
  execAffectedRows: null,
  execError: "column \"active\" does not exist",
};

function makeMeData(permissions: string[]) {
  return { permissions };
}

function makeHooks(permissions: string[]) {
  return {
    meQueryOptions: { queryKey: ["me"] },
    useUpdateRequest: () => ({
      data: fakeDetail,
      isPending: false,
      isError: false,
    }),
    useApproveUpdate: () => ({ mutate: vi.fn(), isPending: false }),
    useRejectUpdate: () => ({ mutate: vi.fn(), isPending: false }),
    useCancelUpdate: () => ({ mutate: vi.fn(), isPending: false }),
    useExecuteUpdate: () => ({ mutate: vi.fn(), isPending: false }),
    // meQueryOptions.queryKey data — simulate via context
    _meData: makeMeData(permissions),
  };
}

// We need to inject meData into the route context mock.
// The component reads it from `Route.useRouteContext()`. We mock that below.
let currentPermissions: string[] = [];

vi.mock("@/api/hooks", () => ({
  meQueryOptions: { queryKey: ["me"] },
  useUpdateRequest: () => ({
    data: fakeDetail,
    isPending: false,
    isError: false,
  }),
  useApproveUpdate: () => ({ mutate: vi.fn(), isPending: false }),
  useRejectUpdate: () => ({ mutate: vi.fn(), isPending: false }),
  useCancelUpdate: () => ({ mutate: vi.fn(), isPending: false }),
  useExecuteUpdate: () => ({ mutate: vi.fn(), isPending: false }),
}));

// Patch Route statics used by the component
vi.mock("@/routes/_authed/update/$id.tsx", async (importOriginal) => {
  const mod = await importOriginal<typeof import("@/routes/_authed/update/$id.tsx")>();
  return mod;
});

afterEach(() => {
  cleanup();
  mockNavigate.mockReset();
  currentPermissions = [];
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

function Wrapper({ children }: { children: React.ReactNode }) {
  return React.createElement(MantineProvider, null, children);
}

describe("UpdateDetailPage — Recreate button", () => {
  it("shows Recreate button when user has update:submit", () => {
    render(
      React.createElement(UpdateDetailPage, {
        requestId: "req-1",
        permissions: ["update:submit"],
      }),
      { wrapper: Wrapper },
    );
    expect(screen.getByRole("button", { name: /recreate/i })).toBeInTheDocument();
  });

  it("hides Recreate button when user lacks update:submit", () => {
    render(
      React.createElement(UpdateDetailPage, {
        requestId: "req-1",
        permissions: ["update:approve"],
      }),
      { wrapper: Wrapper },
    );
    expect(screen.queryByRole("button", { name: /recreate/i })).toBeNull();
  });

  it("navigates to /update/new?from=<id> when Recreate is clicked", () => {
    render(
      React.createElement(UpdateDetailPage, {
        requestId: "req-1",
        permissions: ["update:submit"],
      }),
      { wrapper: Wrapper },
    );
    fireEvent.click(screen.getByRole("button", { name: /recreate/i }));
    expect(mockNavigate).toHaveBeenCalledWith({
      to: "/update/new",
      search: { from: "req-1" },
    });
  });
});
```

- [ ] **Step 2.2 — Run test to verify it fails**

```bash
cd src/frontend && npx vitest run src/routes/_authed/__tests__/update-detail-recreate.test.tsx
```

Expected: FAIL — `UpdateDetailPage` is not exported and has no "Recreate" button.

- [ ] **Step 2.3 — Implement the Recreate button in `$id.tsx`**

In `src/frontend/src/routes/_authed/update/$id.tsx`:

**a)** Add `useNavigate` to the import from `@tanstack/react-router`:

```tsx
import { createFileRoute, redirect, useNavigate } from "@tanstack/react-router";
```

**b)** Export `UpdateDetailPage` and accept testable props (same pattern as `NewUpdatePage`). Replace:

```tsx
function UpdateDetailPage() {
  const { id } = Route.useParams();
  const meData = Route.useRouteContext().queryClient.getQueryData(meQueryOptions.queryKey);
```

With:

```tsx
export function UpdateDetailPage({
  requestId,
  permissions,
}: {
  requestId?: string;
  permissions?: string[];
} = {}) {
  const id: string = (() => {
    try {
      return Route.useParams().id;
    } catch {
      return requestId ?? "";
    }
  })();
  const routePermissions: string[] | undefined = (() => {
    try {
      return Route.useRouteContext().queryClient.getQueryData(meQueryOptions.queryKey)?.permissions;
    } catch {
      return permissions;
    }
  })();
  const meData = { permissions: routePermissions ?? permissions ?? [] };
```

**c)** Add `useNavigate` inside the component (after `meData`):

```tsx
const navigate = useNavigate();
```

**d)** Add the Recreate button to the action `<Group>` (after the Execute button block):

```tsx
{canSubmit && (
  <Button
    variant="light"
    onClick={() => navigate({ to: "/update/new", search: { from: id } })}
  >
    Recreate
  </Button>
)}
```

**e)** Update the `Route` declaration at the top — replace `component: UpdateDetailPage` to point to the now-exported function. No change needed since it's the same name.

- [ ] **Step 2.4 — Run test to verify it passes**

```bash
cd src/frontend && npx vitest run src/routes/_authed/__tests__/update-detail-recreate.test.tsx
```

Expected: PASS (3 tests).

- [ ] **Step 2.5 — Run full test suite to check for regressions**

```bash
cd src/frontend && npm test
```

Expected: all tests pass.

- [ ] **Step 2.6 — Commit**

```bash
git add src/frontend/src/routes/_authed/update/\$id.tsx \
        src/frontend/src/routes/_authed/__tests__/update-detail-recreate.test.tsx
git commit -m "feat: add Recreate button to update request detail page"
```

---

## Self-Review Notes

- `useUpdateRequest` `enabled: id !== ""` guard prevents a stray network call when `from` is absent.
- The `seeded` ref prevents the `useEffect` from overwriting user edits if the query re-fetches.
- The `try/catch` pattern for `Route.useParams()` / `Route.useSearch()` / `Route.useRouteContext()` is intentional — it allows the exported component function to be rendered in tests without a real TanStack Router context, matching the pattern established in `query-clear.test.tsx`.
- No backend changes, no new API endpoints.
- The Recreate button is placed last in the action group so it doesn't visually compete with primary actions (Approve, Execute).
