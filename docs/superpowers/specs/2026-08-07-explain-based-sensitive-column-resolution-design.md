# EXPLAIN-Based Sensitive-Column Resolution — Design

**Date:** 2026-08-07
**Status:** Approved (design), pending implementation plan
**Engine scope:** PostgreSQL only (only concrete `ITargetEngine` on main)

## Overview

The sensitive-column guard blocks queries that reference columns an admin has
marked sensitive (unless the user holds a bypass). Today the guard resolves
columns with a hand-rolled tokenizer (`SqlTokenizer` + `SqlColumnChecker`) that
matches **bare column names** against the sensitive list with no knowledge of
which table each name belongs to. That produces two field-reported defects. This
design replaces the resolution step with the PostgreSQL planner's own output
(`EXPLAIN (VERBOSE, FORMAT JSON, COSTS OFF)`), which resolves names, aliases,
views, and `*` expansion exactly, and keeps the tokenizer as a safe fallback. It
also closes a serialization vector neither mechanism can see — functions that take
the target relation as a string literal (`query_to_xml`, `table_to_xml`, …) — with
an explicit denylist.

## Problem

Both defects share one root cause: **attribution by bare name against the whole
sensitive list** (`SqlColumnChecker.cs:74-83`).

### 1. Wrong-table mis-attribution (reporting bug)

With `email` marked sensitive on both `public.users` and `public.contacts`:

```sql
SELECT email FROM public.contacts
```

The identifier `email` matches every sensitive row of that name, so the guard
reports **`public.users.email`** blocked/touched even though the query never
referenced `users`. This pollutes the 403 payload and the audit `Touched` list
(`SensitiveColumnGuard.cs:40-42`, `:56-58`). No existing test pins this behavior.

### 2. Over-blocking (false positives)

- `SELECT *` blocks **every** sensitive column in the database, not just those on
  the queried tables (`SqlColumnChecker.cs:37-44` ignores the FROM clause).
- Any bare name match anywhere blocks — `SELECT email FROM newsletter` blocks even
  when only `users.email` is sensitive.
- `u.*` is treated as a wildcard and blocks database-wide.

The over-block behavior is currently pinned by tests and documented as
intentional (`SqlColumnCheckerTests.cs:204-215`), so relaxing it is a deliberate
policy change, not just a bug fix.

## Approach

**Chosen — EXPLAIN-based resolver behind `ITargetEngine`.** Ask the planner to
resolve the statement (`EXPLAIN (VERBOSE, FORMAT JSON, COSTS OFF)` — planning
only, no execution, safe even for writes), read the resolved base-table columns
out of the plan, and intersect with the sensitive set. Attribution comes from the
plan's real `Schema`/`Relation Name`, eliminating both defects and, as a bonus,
closing the view-indirection false-negative the tokenizer cannot see.

**Rejected:**

- **AST parser (`SqlParser-cs`) + our own schema binder / view expansion.**
  Reinvents name resolution the planner already does, still weak on nested
  views/functions, more code to maintain. Its one advantage (no per-query DB
  round-trip) does not outweigh the accuracy gap.
- **Column-level `REVOKE` on the read role.** Strongest enforcement (Postgres
  rejects the query itself, and it catches at execution even the text-argument
  functions the denylist handles by name), but reworks credential provisioning,
  loses the structured per-column 403 detail, and complicates per-user bypass. A
  possible future hardening layer, not this fix.

## Architecture

### 1. Core contract (`SluiceBase.Core/Targets`)

Add one method to `ITargetEngine` and one record:

```csharp
// ITargetEngine.cs — single statement in, resolved base-table columns out.
Task<IReadOnlyList<ColumnRef>> ResolveReferencedColumnsAsync(
    string connectionString, string sql, CancellationToken ct);

public sealed record ColumnRef(string Schema, string Table, string Column);
```

**Contract:**
- Input is a **single** statement.
- Returns every base-table column the statement reads or writes, fully qualified
  (schema + table + column) as the planner resolved them.
- **Throws** when the statement cannot be planned (syntax error, missing object,
  non-EXPLAIN-able statement). Callers decide fallback; the guard falls back to
  the tokenizer.
- Keeps all Npgsql behind the interface per CLAUDE.md.

### 2. Postgres implementation (`PostgresTargetEngine` + `PostgresPlanColumnExtractor`)

`ResolveReferencedColumnsAsync` runs
`EXPLAIN (VERBOSE, FORMAT JSON, COSTS OFF) <stmt>` inside the same read-only,
always-rolled-back transaction pattern as the existing `ExplainAsync`
(`PostgresTargetEngine.cs:569-597`). **No `ANALYZE`** — planning does not execute
the statement, so there is no mutation even for `INSERT`/`UPDATE`/`DELETE` and no
result rows are read.

A new `PostgresPlanColumnExtractor` (sibling to `PostgresPlanParser`) walks the
plan JSON:

1. **Build an alias map.** For every node carrying `Schema` + `Relation Name` +
   `Alias`, record `alias → (schema, relation)`. Postgres disambiguates repeated
   relations (`users`, `users_1`), so aliases are unique across the plan.
2. **Collect column references** at every node from `Output` **and** every
   expression-bearing field — `Filter`, `Index Cond`, `Recheck Cond`, `Hash Cond`,
   `Merge Cond`, `Join Filter`, `Sort Key`, `Group Key`, `Cache Key`, `TID Cond`,
   `One-Time Filter`. `Output` alone misses WHERE/JOIN-only columns (they are
   consumed by a node's Filter, not emitted upward), so the expression fields are
   required.
3. **Resolve** each `alias.column` (and `"quoted".col`) token through the alias
   map into a `ColumnRef`. Matching against the sensitive set is case-insensitive,
   matching current behavior.

**Whole-row handling (validate empirically).** For `to_jsonb(u)`, `SELECT u`,
`xmlelement(name r, u.*)`, etc., the scan node's `Output` is expected to enumerate
the real columns the planner must read. This will be **verified against the live
stack** during implementation. If any form surfaces as an un-enumerated
whole-row variable, the extractor falls back to flagging the whole relation (the
guard then treats all of that relation's sensitive columns as touched) — never
under-reporting.

### 3. Statement splitting (`SqlStatementSplitter`)

A small splitter reusing `SqlTokenizer`'s existing string / line-comment /
block-comment / dollar-quote scanning to split SQL on **top-level** semicolons.
The guard EXPLAINs each statement and unions the resolved columns. This keeps the
multi-statement update-preview path accurate.

### 4. Serialization-function denylist (`SerializationFunctionDenylist`)

A class of PostgreSQL functions takes the target relation or query as a **string
literal / regclass argument** and serializes whole rows — the referenced columns
never appear as parseable identifiers, so **both** EXPLAIN (a bare `Function Scan`)
and the tokenizer (which skips string contents) are blind to them. Left unhandled,
`query_to_xml('SELECT ssn FROM users', …)` would exfiltrate a sensitive column
past the guard. These are closed with an explicit denylist rather than resolution.

Denylisted functions (the XML export family, `pg_catalog`):

```
table_to_xml, table_to_xmlschema, table_to_xml_and_xmlschema,
query_to_xml, query_to_xmlschema, query_to_xml_and_xmlschema,
cursor_to_xml, cursor_to_xmlschema,
schema_to_xml, schema_to_xmlschema, schema_to_xml_and_xmlschema,
database_to_xml, database_to_xmlschema, database_to_xml_and_xmlschema
```

**Detection** reuses `SqlTokenizer`: any identifier token equal (case-insensitive)
to a denylisted name is a hit. A relation/column literally named after one of
these builtins is implausible; the false-positive risk is negligible and the
safe direction.

**Policy:** the denylist only bites when the database has ≥1 sensitive column
(the guard already early-outs otherwise), so it never affects databases without
sensitive data. A hit blocks the whole query — there is no per-column bypass for
an opaque whole-relation dump, so the block ignores bypass grants. Analyst
queries effectively never use these functions, so the conservative posture is
cheap.

### 5. Guard rewrite (`SensitiveColumnGuard`)

Inject `IServerConnectionFactory` and `ITargetEngineRegistry`. The call-site
signature stays `EvaluateAsync(userId, databaseId, sql, ct)`. The decision record
gains **one optional field** for the denylist path:

```csharp
internal sealed record SensitiveColumnDecision(
    IReadOnlyList<SensitiveColumnHit> BlockedHits,
    IReadOnlyList<string> Touched,
    string? PolicyBlockReason = null);   // set when a denylisted function blocks
```

A query is blocked when `BlockedHits.Count > 0 || PolicyBlockReason is not null`.
Existing consumers that only read `BlockedHits`/`Touched` keep compiling; the
block-response path is updated to surface the reason (see Error Handling).

New flow:

1. Load sensitive columns for the database (unchanged). If none, early-out.
2. **Denylist scan** the tokenized SQL for a serialization function (§4). On a
   hit, return immediately with `PolicyBlockReason` naming the function and empty
   `BlockedHits`/`Touched` — resolution is neither needed nor trustworthy here.
3. Resolve the read connection string
   (`IServerConnectionFactory.GetConnectionStringAsync(databaseId, CredentialKind.Read, ct)`)
   and the engine (`ITargetEngineRegistry.Resolve(server.Kind)`).
4. Split the SQL into statements.
5. Per statement, call `ResolveReferencedColumnsAsync`:
   - **Success** → intersect resolved `ColumnRef`s with the sensitive set
     (case-insensitive) → contributes to `Touched`; minus the user's bypasses →
     contributes to `BlockedHits`.
   - **Failure** (throws) → fall back to `SqlColumnChecker.FindBlockedColumns`
     for that statement (conservative over-block, never under-block).
6. Union across statements and return the decision.

Both `Touched` (audit) and `BlockedHits` (block/403) become accurate on the
resolved path.

## Error Handling & Security Posture

- **Resolver failure → tokenizer fallback** (decided). The tokenizer stays in the
  tree as the safe net; its over-block only re-appears for input EXPLAIN cannot
  analyze. This preserves the invariant that the guard never under-blocks.
- **EXPLAIN privileges.** Planning requires `SELECT` on the referenced tables,
  which the read credential already holds (it is about to run the query).
- **Cost.** One extra plan-only round-trip per query. Acceptable; a future
  optimization can merge it with the advisory auto-explain estimate at
  `IQueryService.cs:168`. Out of scope here (YAGNI).

### Denylist block-response plumbing

A denylist block carries a reason, not columns, through the existing block path:

- **`IQueryService`** — `AccessResult` gains the reason; `CheckAccessAsync` treats
  `BlockedHits.Count > 0 || PolicyBlockReason is not null` as `Blocked`. The
  `query_log` `Error` records the reason (in place of the
  `"Sensitive columns: …"` string); `SensitiveColumns` (touched) is empty.
- **403 response** (`QueryEndpoints`, both `/api/query` and `/api/query/explain`)
  — keep `type: "sensitive_columns"`, emit `columns: []`, and add
  `extensions.reason` with the message. Concrete-column blocks are unchanged
  (`reason` absent, `columns` populated).
- **Frontend** — the 403 handler renders the `reason` when `columns` is empty;
  the existing column-list rendering is otherwise unchanged.
- **MCP** — `SensitiveColumnBlockPayload` gains an optional reason; on a denylist
  block, `blockedColumns` is empty and the `error`/`guidance` convey the reason.
  The deterrence guidance already tells agents not to work around blocks, so no
  new column identities leak.

## Decisions (locked)

- EXPLAIN resolver is authoritative; tokenizer (`SqlTokenizer` +
  `SqlColumnChecker`) is retained as the fallback, not deleted.
- On any resolution failure, **fall back to the tokenizer**.
- Multi-statement SQL is **split and EXPLAINed per statement**, results unioned.
- `EXPLAIN` runs **without `ANALYZE`** (plan only), in a read-only rolled-back
  transaction.
- Guard owns the round-trip internally; call-site signatures are unchanged and
  `SensitiveColumnDecision` gains only an optional `PolicyBlockReason`.
- Text/regclass-argument serializers (XML export family) are **blocked by an
  explicit denylist**, active only when the database has sensitive columns.

## Testing

### Existing tests

`SqlColumnCheckerTests` stay — the tokenizer remains the fallback and is still
unit-tested. Endpoint-level tests are updated only where the corrected
attribution changes an assertion.

### New integration tests (Testcontainers Postgres)

`SensitiveColumnResolutionTests`, seeding real relations so the planner resolves
them. Core correctness cases:

| Scenario | Setup | Expected |
|---|---|---|
| Mis-attribution fix | `email` sensitive on `users` only; `contacts` also has `email` | `SELECT email FROM contacts` → **not blocked**, `Touched` excludes `users.email` |
| Attribution correctness | `email` sensitive on **both** `users` and `contacts` | `SELECT email FROM contacts` → blocks **`contacts.email` only** |
| `SELECT *` over-block fix | `users.email` sensitive; `orders.amount` sensitive | `SELECT * FROM users` → blocks `users.email` only, **not** `orders.amount` |
| View see-through | view `v_users` selects `users.ssn`; `ssn` sensitive | `SELECT masked FROM v_users` → blocks `users.ssn` |
| WHERE-only attribution | `users.ssn` sensitive | `SELECT id FROM users WHERE ssn IS NOT NULL` → blocks `users.ssn` |
| Alias resolution | `users.ssn` sensitive | `SELECT u.id FROM users u WHERE u.ssn IS NOT NULL` → blocks `users.ssn` |
| Multi-statement union | `users.email`, `orders.amount` sensitive | `SELECT email FROM users; SELECT amount FROM orders;` → blocks both, correctly attributed |
| Unplannable → fallback | `users.email` sensitive | syntactically invalid SQL → tokenizer fallback still blocks (no under-block) |

### Brute-force serialization battery

The point of this battery is to prove the resolver **never under-blocks** across
many whole-row / serialization forms, and that EXPLAIN degrades safely where it
genuinely cannot see through. Seed a `users(id, name, email, ssn)` table with
`email` + `ssn` sensitive.

**Expression-argument serializers — EXPLAIN must resolve precisely** (block the
seeded sensitive columns of `users`, and nothing from other tables):

- JSON: `to_jsonb(u)`, `to_json(u)`, `row_to_json(u)`, `json_agg(u)`,
  `jsonb_agg(u)`, `jsonb_build_object('e', u.email)`,
  `array_to_json(array_agg(u))`
- XML: `xmlelement(name r, u.*)`, `xmlforest(u.email AS email, u.ssn AS ssn)`,
  `xmlagg(xmlelement(name r, u.*))`, `xmlconcat(xmlelement(name e, u.email))`
- hstore: `hstore(u)`
- Whole-row / composite: `SELECT u FROM users u`, `SELECT u::text FROM users u`,
  `array_agg(u)`, `ROW(u.*)`
- Wildcard forms: `SELECT u.* FROM users u`, `SELECT * FROM users`
- Encoding wrappers over a column:
  `encode(convert_to(u.email, 'UTF8'), 'base64')`

For each, assert the resolved set contains `users.email` + `users.ssn` and no
column from any other seeded table.

**Text/regclass-argument serializers — blocked by the denylist (§4).** Each is
opaque to EXPLAIN and the tokenizer, so the denylist must catch them:
`table_to_xml('users', true, false, '')`,
`query_to_xml('SELECT ssn FROM users', true, false, '')`,
`query_to_xmlschema(...)`, `cursor_to_xml(...)`, `schema_to_xml('public', …)`,
`database_to_xml(…)`, plus case/whitespace variants (`Query_To_XML`,
`table_to_xml (…)`). Assert each returns a **policy block** with a
`PolicyBlockReason` naming the function and **no** fabricated column list. Also
assert that when the database has **no** sensitive columns, the same query is
**not** blocked (denylist is dormant), and that a denylisted name appearing only
inside a string literal or comment does **not** trip the block (tokenizer skips
those).

**Robustness / non-crash inputs** (resolver must not throw uncaught; fallback
engages cleanly): deeply nested subqueries, CTEs and recursive CTEs, `UNION`/
`INTERSECT` over sensitive and non-sensitive tables, lateral joins, window
functions over sensitive columns, `DISTINCT ON (u.ssn)`, comments and
dollar-quoted bodies embedded mid-query.

## Live demonstration (Aspire stack + seed data)

Separate from the automated tests, to demonstrate the fix in the running app:

1. Start the Aspire stack.
2. Seed the target Postgres with `users(email, ssn)`, `contacts(email)`, and a
   view over `users`. Mark `users.email` sensitive; leave `contacts.email`
   unmarked.
3. Log in (alice/dev) and confirm `SELECT email FROM contacts` is neither blocked
   nor reported as `users.email`, and `SELECT * FROM users` blocks only
   `users.email`.

## Known Limitations & Follow-up

- **Opaque PL/pgSQL / `SECURITY DEFINER` functions** that read sensitive columns
  internally show as `Function Scan` with no column detail — unchanged from today.
  A custom function is not on the serialization denylist unless explicitly added;
  the general case (arbitrary user-defined functions) remains outside static
  analysis. Column-level `REVOKE` on the read role is the durable answer and is
  parked as a future hardening layer.

## Out of Scope

- Column-level `REVOKE` enforcement layer.
- Extending the denylist beyond the XML export family (e.g. user-defined
  functions, `dblink`) — the family named in §4 is the closable set for this
  change.
- Merging the guard's EXPLAIN round-trip with the advisory auto-explain estimate.
- Non-Postgres engines (none on main).
