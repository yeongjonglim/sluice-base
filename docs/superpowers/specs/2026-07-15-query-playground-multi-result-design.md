# Query Playground — Multi-Statement Execution & Result Tabs

**Date:** 2026-07-15
**Route:** `src/frontend/src/routes/_authed/query/index.tsx`
**Scope:** Frontend-only. No backend, engine, OpenAPI, or `schema.ts` changes.

## Goal

Turn the query page into a playground-like experience: the user can run one
statement, the statement under the cursor, a highlighted selection, or every
statement in the editor. Each executed statement produces its own result set,
shown as a tab whose label identifies the source statement and which flashes the
originating lines in the editor when clicked.

## Why frontend-only (result model)

The backend `/api/query` endpoint runs **one** SQL string per call and returns
**one** result set (`Columns`, `Rows`, `RowCount`, `DurationMs`, `Error`). Every
call independently: writes one `QueryLog` audit row, enforces sensitive-column
policy via `SqlColumnChecker` on that statement's text, applies the per-call
timeout, and checks `query:execute` permission.

Postgres *can* return multiple result sets natively (simple-query protocol,
`NpgsqlDataReader.NextResult()`), but that path runs the whole batch as one
implicit transaction with one audit identity and all-or-nothing failure. That
fights SluiceBase's per-statement audit + governance model.

**Decision: split statements client-side and fire one `/api/query` call per
statement.** This preserves per-statement audit logging, sensitive-column
gating, timeout, and failure isolation exactly as they work today — it is the
model that fits the governed-gateway design, not a workaround.

**Accepted consequence:** each statement runs on its own pooled connection, so
cross-statement session state does not carry (`SET`, temp tables, explicit
`BEGIN/COMMIT`, session vars won't span statements). Acceptable for a read-
oriented governed query gateway.

## Run resolution model

The whole editor is always split into statements. Each run path picks a subset
of that split; nothing runs a raw byte-range.

- **Selection run** (`Cmd/Ctrl+Enter` with a selection): run every statement
  whose range **intersects** the selection. A statement runs **in full even if
  only partially highlighted** — selection is a *pointer to statements*, not the
  literal bytes executed. N touched statements → N calls → N tabs.
- **Statement-at-cursor** (`Cmd/Ctrl+Enter`, no selection): run the single
  statement whose range contains the cursor.
- **Run all** (`Shift+Cmd/Ctrl+Enter`): run every statement.

Because all three paths converge on the same `RunEntry` shape carrying absolute
editor coordinates, click-a-tab → flash-source-lines works identically
regardless of how the run was launched. The splitter and the highlighter share
one coordinate system.

## Dispatch

Statements run **in parallel**, capped at **6 concurrent** calls; the remainder
queue. Parallel is fastest wall-clock and, on a governed gateway, each
concurrent call is independently audited and permission-checked. The cap
prevents a large scratchpad from opening many pooled DB connections at once.
Tabs are ordered by `fromPos` and stay stable regardless of completion order.

## Result tabs — provenance

Each tab identifies its source statement via **snippet + click-to-highlight**:

- Label: truncated statement snippet (~40 chars) + row count, or a red error
  dot on failure.
- Hover: tooltip with the full statement text.
- Click: `editor.dispatch(scrollIntoView + a temporary flash decoration on
  [fromPos, toPos])`, scrolling to and flashing the source lines.

## Component breakdown

Favor small, testable units over today's single 364-line route file.

| Unit | Responsibility | Depends on |
|---|---|---|
| `utils/splitSqlStatements.ts` | Pure: SQL string → `Array<{ text, fromPos, toPos, fromLine, toLine }>`. Handles `;`, `'…'` literals (`''` escape), `$$…$$` / `$tag$…$tag$` dollar-quoting, `--` line comments, `/* */` block comments. **TDD** — the one genuinely tricky unit. | none |
| `utils/statementAtCursor.ts` | Split output + cursor pos → containing statement (and: split output + selection range → intersecting statements). | splitter output |
| `hooks/useQueryRuns.ts` | Owns `Array<RunEntry>`; dispatches ≤6 concurrent `/api/query` calls; patches each entry's `status: pending \| success \| error \| blocked`, response, error, and source metadata. Replaces the single `useExecuteQuery` mutation. | `apiRequest` |
| `components/query/ResultTabs.tsx` | One Mantine tab per run (snippet + row count + error dot, tooltip = full statement, click → highlight callback). Hosts the active `ResultGrid`. | run entries, highlight fn |
| `components/query/ResultGrid.tsx` | Existing grid / query-error / blocked-columns / CSV rendering, extracted per-run from today's `QueryResults`. | one run entry |
| `routes/_authed/query/index.tsx` | Wiring: editor, keymaps (`Cmd+Enter` = selection→statement; `Shift+Cmd+Enter` = all), `useQueryRuns`, editor-highlight fn via `editorRef`. | all of the above |

### RunEntry shape

```ts
interface RunEntry {
  id: string;          // stable key
  index: number;       // editor order
  text: string;        // statement SQL sent to /api/query
  fromPos: number;     // absolute editor char offset
  toPos: number;
  fromLine: number;    // 1-based, for labels/highlight
  toLine: number;
  status: "pending" | "success" | "error" | "blocked";
  response: ExecuteQueryResponse | null;
  error: unknown;      // transport/blocked error payload
}
```

## Data flow (one run cycle)

```
keypress → resolve target statements (split → subset by selection/cursor/all)
         → useQueryRuns.run(statements)
              → clear prior runs; create RunEntry[] all status:"pending"
              → dispatch, ≤6 concurrent, each POST /api/query {databaseId, sql: entry.text}
              → on each settle: patch entry → success | error | blocked
         → ResultTabs renders tabs (sorted by fromPos, stable across finish order)
              → active tab → ResultGrid (grid | query-error alert | blocked-columns alert | CSV)
              → click tab → editor scrollIntoView + flash [fromPos, toPos]
```

## Edge cases & decisions

- **Empty / whitespace-only / all-comments editor** → splitter yields zero
  statements → Run is a no-op (button stays disabled, as today).
- **Trailing statement with no `;`** → still a valid statement; do not require a
  terminator.
- **No database selected** → Run disabled (unchanged).
- **Blocked (403 sensitive columns)** → per-tab: that tab shows the existing
  orange blocked-columns alert; sibling tabs unaffected (parallel isolation).
- **Query error** → per-tab red alert with `durationMs`, as today.
- **Re-running** → replaces the previous run set entirely (fresh tabs). No
  accumulation / no pinned history across runs (out of scope for v1).
- **CSV export** → per active tab, unchanged behavior.
- **`sluice:query:editor` session persistence** → unchanged; runs are ephemeral
  and not persisted.
- **Concurrency cap** → 6 in flight; the rest queue.

## Testing

- `splitSqlStatements` — TDD, unit tests covering: multiple `;`-separated
  statements; `;` inside `'…'` and `''`-escaped literals; `$$…$$` and tagged
  `$tag$…$tag$` dollar-quoted bodies containing `;`; `--` and `/* */` comments
  containing `;`; trailing statement without terminator; empty / comment-only
  input; correct `fromPos/toPos/fromLine/toLine` offsets.
- `statementAtCursor` — cursor inside / at boundary of a statement; selection
  intersecting one, several, and partial statements (expands to full).

## Out of scope (v1)

- Backend multi-result-set contract.
- Persisted / pinned result history across runs.
- Multiple editor documents/tabs (scratchpad files).
- Auto-selecting a tab when the editor cursor enters its range (possible later
  since each entry knows its range).
