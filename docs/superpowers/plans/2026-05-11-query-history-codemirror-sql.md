# Query History CodeMirror SQL Display Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the truncated `<Code>` SQL preview in the query history table with a full, read-only, syntax-highlighted CodeMirror editor, and move the copy button to sit beside it.

**Architecture:** Single-file change to `src/frontend/src/routes/_authed/query/history.tsx`. Merge the SQL cell and trailing copy-button cell into one `<Table.Td>` containing a `<Group>` with a CodeMirror editor (flex-grow, auto-height, read-only) and the copy `<ActionIcon>` pinned to its right. No API or hook changes.

**Tech Stack:** React, `@uiw/react-codemirror`, `@codemirror/lang-sql`, `@uiw/codemirror-themes-all` (githubDark/githubLight), Mantine v7

---

### Task 1: Add CodeMirror imports to history.tsx

**Files:**
- Modify: `src/frontend/src/routes/_authed/query/history.tsx:1-21`

This task has no testable logic — verify via TypeScript compilation after each task.

- [ ] **Step 1: Add four new imports**

Open `src/frontend/src/routes/_authed/query/history.tsx`. After the existing import block (currently ending around line 21), add the following imports. Keep them grouped with the other external library imports:

```tsx
import CodeMirror from "@uiw/react-codemirror";
import { sql } from "@codemirror/lang-sql";
import { githubDark, githubLight } from "@uiw/codemirror-themes-all";
import { useComputedColorScheme } from "@mantine/core";
```

The full import block at the top of the file should now look like:

```tsx
import {
  ActionIcon,
  Alert,
  Badge,
  Code,
  Group,
  ScrollArea,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  useComputedColorScheme,
} from "@mantine/core";
import { notifications } from "@mantine/notifications";
import { IconCopy } from "@tabler/icons-react";
import { createFileRoute, redirect, useNavigate } from "@tanstack/react-router";
import { useState } from "react";
import CodeMirror from "@uiw/react-codemirror";
import { sql } from "@codemirror/lang-sql";
import { githubDark, githubLight } from "@uiw/codemirror-themes-all";
import { meQueryOptions, useQueryHistory, useServers } from "@/api/hooks";
import type { QueryHistoryItem, QueryHistoryFilters } from "@/api/hooks";
import { useHasPermission } from "@/auth/permission";
```

Note: `useComputedColorScheme` is added to the existing `@mantine/core` destructured import — do not leave the separate `import { useComputedColorScheme }` line below; merge it into the existing Mantine import block.

- [ ] **Step 2: Remove the now-unused `Code` import from Mantine**

The `<Code>` component will no longer be used after Task 3. Remove it from the `@mantine/core` import destructure now to avoid a lint warning:

```tsx
import {
  ActionIcon,
  Alert,
  Badge,
  Group,
  ScrollArea,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  useComputedColorScheme,
} from "@mantine/core";
```

- [ ] **Step 3: Verify TypeScript compiles**

Run from `src/frontend/`:
```bash
npx tsc --noEmit
```

Expected: exits 0 with no errors. If it reports `Module not found` for any CodeMirror package, run `npm install` in `src/frontend/` first — the packages are already listed in `package.json`.

---

### Task 2: Update table header and remove nowrap style

**Files:**
- Modify: `src/frontend/src/routes/_authed/query/history.tsx` — `QueryHistoryPage` component

- [ ] **Step 1: Remove `whiteSpace: "nowrap"` from the Table**

Find the `<Table>` element in `QueryHistoryPage` (around line 161). Change:

```tsx
<Table striped withTableBorder highlightOnHover fz="sm" style={{ whiteSpace: "nowrap" }}>
```

to:

```tsx
<Table striped withTableBorder highlightOnHover fz="sm">
```

- [ ] **Step 2: Merge the SQL and trailing copy-button header columns**

In `<Table.Thead>`, find these two consecutive `<Table.Th>` elements:

```tsx
<Table.Th>SQL</Table.Th>
<Table.Th></Table.Th>
```

Replace them with a single header cell:

```tsx
<Table.Th>SQL</Table.Th>
```

The full `<Table.Thead>` should now be:

```tsx
<Table.Thead>
  <Table.Tr>
    <Table.Th>Status</Table.Th>
    <Table.Th>Database</Table.Th>
    {canAudit && <Table.Th>User</Table.Th>}
    <Table.Th>SQL</Table.Th>
    <Table.Th>Executed At</Table.Th>
    <Table.Th>Duration</Table.Th>
    <Table.Th>Rows</Table.Th>
  </Table.Tr>
</Table.Thead>
```

- [ ] **Step 3: Verify TypeScript compiles**

```bash
npx tsc --noEmit
```

Expected: exits 0.

---

### Task 3: Rewrite HistoryRow SQL cell with CodeMirror

**Files:**
- Modify: `src/frontend/src/routes/_authed/query/history.tsx` — `HistoryRow` component

- [ ] **Step 1: Add `useComputedColorScheme` hook to `HistoryRow`**

At the top of the `HistoryRow` function body, add:

```tsx
const colorScheme = useComputedColorScheme();
```

- [ ] **Step 2: Remove the `sqlPreview` truncation variable**

Delete this block (around line 195):

```tsx
const sqlPreview = item.queryText.length > 80
  ? `${item.queryText.slice(0, 80)}…`
  : item.queryText;
```

- [ ] **Step 3: Replace the SQL cell and the trailing copy cell with one merged cell**

Find and delete both of these `<Table.Td>` blocks from the return JSX:

```tsx
<Table.Td style={{ maxWidth: 400 }}>
  <Code fz="xs">{sqlPreview}</Code>
</Table.Td>
```

and:

```tsx
<Table.Td>
  <ActionIcon size="sm" variant="subtle" onClick={copySql} aria-label="Copy SQL">
    <IconCopy size={14} />
  </ActionIcon>
</Table.Td>
```

Replace them with a single `<Table.Td>` containing both:

```tsx
<Table.Td style={{ minWidth: 300, maxWidth: 600 }}>
  <Group gap="xs" align="flex-start" wrap="nowrap">
    <div style={{ flex: 1 }}>
      <CodeMirror
        value={item.queryText}
        readOnly
        editable={false}
        extensions={[sql()]}
        theme={colorScheme === "dark" ? githubDark : githubLight}
        height="auto"
        basicSetup={{ lineNumbers: false, foldGutter: false }}
      />
    </div>
    <ActionIcon size="sm" variant="subtle" onClick={copySql} aria-label="Copy SQL">
      <IconCopy size={14} />
    </ActionIcon>
  </Group>
</Table.Td>
```

The `copySql` function defined earlier in `HistoryRow` remains unchanged — it still reads `item.queryText`.

- [ ] **Step 4: Verify the full `HistoryRow` return JSX**

The complete return from `HistoryRow` should now look like:

```tsx
return (
  <Table.Tr>
    <Table.Td>
      <Badge color={STATUS_COLOR[item.status] ?? "gray"} size="sm">
        {item.status}
      </Badge>
    </Table.Td>
    <Table.Td>{item.databaseDisplayName ?? "—"}</Table.Td>
    {canAudit && <Table.Td>{item.userName ?? "—"}</Table.Td>}
    <Table.Td style={{ minWidth: 300, maxWidth: 600 }}>
      <Group gap="xs" align="flex-start" wrap="nowrap">
        <div style={{ flex: 1 }}>
          <CodeMirror
            value={item.queryText}
            readOnly
            editable={false}
            extensions={[sql()]}
            theme={colorScheme === "dark" ? githubDark : githubLight}
            height="auto"
            basicSetup={{ lineNumbers: false, foldGutter: false }}
          />
        </div>
        <ActionIcon size="sm" variant="subtle" onClick={copySql} aria-label="Copy SQL">
          <IconCopy size={14} />
        </ActionIcon>
      </Group>
    </Table.Td>
    <Table.Td>
      <Text size="xs">
        {new Intl.DateTimeFormat("en", { dateStyle: "medium", timeStyle: "short" })
          .format(new Date(item.executedAt))}
      </Text>
    </Table.Td>
    <Table.Td>
      {item.durationMs != null ? (
        <Text size="xs">{item.durationMs} ms</Text>
      ) : "—"}
    </Table.Td>
    <Table.Td>
      {item.rowCount != null ? (
        <Text size="xs">{item.rowCount}</Text>
      ) : "—"}
    </Table.Td>
  </Table.Tr>
);
```

- [ ] **Step 5: Verify TypeScript compiles**

```bash
npx tsc --noEmit
```

Expected: exits 0. If you see a TS error on `readOnly` or `editable`, confirm you're using `@uiw/react-codemirror` v4 — both props exist in v4.

---

### Task 4: Manual verification and commit

**Files:**
- Verify: `src/frontend/src/routes/_authed/query/history.tsx`

- [ ] **Step 1: Start the dev server**

From the repo root, start the Aspire AppHost (or just the frontend if running standalone):

```bash
cd src/frontend && npm run dev
```

Navigate to `/query/history` in the browser.

- [ ] **Step 2: Verify these scenarios**

Check each manually:

1. **Single-line SQL** — row is compact, one line of highlighted SQL visible, copy button to its right
2. **Multi-line SQL** — row expands vertically to show all lines, no vertical scrollbar on the editor
3. **Copy button** — clicking it shows the "SQL copied to clipboard" toast; pasting shows the full untruncated SQL
4. **Dark mode** — toggle dark mode in the app; the editor switches to `githubDark` theme
5. **Light mode** — editor shows `githubLight` theme
6. **Audit user** — log in as a user with `query:audit` permission; the User column renders correctly alongside the SQL cell

- [ ] **Step 3: Commit**

```bash
git add src/frontend/src/routes/_authed/query/history.tsx
git commit -m "feat: replace Code snippet with read-only CodeMirror SQL editor in query history"
```
