# Unified Permission Management Design

**Date:** 2026-05-30  
**Status:** Design phase  
**Branch:** `feat/group-permission-management`

## Overview

Consolidate permission management (`Permission`, `Groups`, `Access` pages) into a single, coherent admin surface called **Access**. This surface presents two coordinated views — **By Principal** (user/group-centric) and **By Resource** (database-centric) — over a unified permission model that covers global grants, group memberships, database-scoped roles, and sensitive-column bypasses. A merged admin permission (`permission:manage` only) gates the whole surface, eliminating the `group:manage` / `permission:manage` split.

## Goals

1. **Reduce admin cognitive load.** One surface, clear navigation, no jumping between pages to answer related questions.
2. **Show effective permissions transparently.** Admins see the full, inherited permission set with provenance (direct vs. which groups).
3. **Support both user-centric and resource-centric workflows.** "What can Bob do?" and "Who can touch Database X?" both answered in one place.
4. **Extend group support to all permission layers.** Groups are first-class throughout: global, db-scoped, and column-bypass.

## Permission Model

Three layers, all manageable via the unified surface:

1. **Global permissions** (`permission:manage`, `server:manage`, `query:execute`, etc.)
   - Granted directly to users or groups.
   - Users can inherit from groups.
   - `permission:manage` is the only admin permission; `group:manage` is removed.

2. **Database-scoped roles** (scopeable: `query:execute`, `query:audit`, `update:submit`, `update:approve`, `update:execute`)
   - Granted per-database to users or groups.
   - Users see effective set (direct + group-inherited).

3. **Sensitive-column bypasses**
   - Granted per-column to users or groups.
   - Users see effective set (direct + group-inherited).

Editing always targets the **direct-to-principal** layer; the view shows effective (direct + inherited) with source badges for transparency.

## IA & Navigation

**Single nav item:** `Access` (icon `IconKey`, route `/access`) replaces `Permission`, `Groups`, and `Access`.

**Old routes redirect:** `/permission` and `/group` redirect into `/access` with a tab/segment pre-selected so bookmarks don't break. `/access` is the new surface (it already exists on this branch and is extended in place, not recreated).

**Tab state lives in search params.** The `/access` route declares `validateSearch` for `{ tab?: "principal" | "resource" | "columns"; segment?: "users" | "groups" }`, and `<Tabs>` is *controlled* via `value={search.tab ?? "principal"}` with `onChange` calling `navigate({ search })`. Redirects must pass params via the typed `search` object — e.g. `redirect({ to: "/access", search: { tab: "principal", segment: "groups" } })` — **not** by embedding a query string in `to` (TanStack Router's typed routing ignores that). Without this wiring the tab/segment params are inert.

**Admin gate:** `permission:manage` only. The `group:manage` permission is removed entirely.

## Three-Tab Surface

### Tab 1: By Principal

**Left pane — Principal list**
- Searchable, segmented toggle: `Users | Groups`
- Each row: Avatar + name/email + secondary info (group count for users, member count for groups)
- Hover action: Delete principal (with confirmation; disabled for "you")
- Click to select; detail pane updates on the right
- Empty state if no principal selected

**Right pane — Principal detail** (stacked sections)

#### Section: Global Permissions
- **For users:** Read-only list of effective global permissions. Each permission shows:
  - Permission name (e.g., `query:execute`)
  - Inline **off** toggle if permission is held directly (removes direct grant only)
  - **Via Group-Name** read-only badge if inherited (cannot be removed from here)
  - If held both directly and via groups: toggle visible, badge visible, toggling removes direct only
- **For groups:** Toggleable list of permissions the group holds (no effective/inherited distinction at group level)

#### Section: Group Memberships (users only)
- Read-only list of groups the user is a member of
- Remove button per group (calls `useRemoveGroupMember`)
- Italic "Not in any groups" if empty

#### Section: Database-Scoped Roles
- Table: rows = databases, columns = scopeable permissions
- **For users:** 
  - Checkboxes show effective state (checked if held directly or via group)
  - Directly-held: regular checkbox (on/off)
  - Group-inherited only: indeterminate checkbox 🔷 with tooltip "via Engineering"
  - Held both ways: checked box + "via Engineering" badge; toggling removes direct grant only
  - Edit calls `useAssignDatabaseRole` / `useRemoveDatabaseRole` with userId
- **For groups:** Plain checkboxes for roles the group holds; edit calls group variants

#### Section: Column Bypasses
- Table: rows = (server/database/column), columns = (bypass on/off)
- Same effective/edit-direct pattern as database-scoped roles
- Edit calls `useAssignColumnBypass` / `useRevokeColumnBypass` (or group variants)

#### Section: Edit User (users only)
- Pencil icon top-right
- Modal to edit user name/email
- "Update" button saves via new `useUpdateUser` hook

#### Section: Members (groups only)
- Combobox "Add member…" to add users to the group (calls `useAddGroupMember`)
- List of current members with avatar + email/name + remove button (calls `useRemoveGroupMember`)

---

### Tab 2: By Resource

**Left pane — Resource list**
- Servers + databases in a hierarchy (always expanded for simplicity, or collapsible)
- Database rows clickable; `isDisabled` state shown muted
- A **"Global"** pseudo-resource at the top for managing global permissions
- Click to select; matrix updates on the right

**Right pane — Resource matrix**

For a **database** resource:
- **Rows:** Principals split into two blocks: **Users** and **Groups**
  - Each row: Avatar + name/email, secondary line showing if the principal has inherited roles here ("via Engineering")
- **Columns:** Scopeable permissions (`query:execute`, `query:audit`, etc.)
- **Cells:**
  - User rows: Checkbox shows effective state (checked if direct or inherited), indeterminate 🔷 if inherited-only, tooltip "via Engineering" or "via Engineering + direct"
  - Clicking a user cell: edit direct layer only (calls `useAssignDatabaseRole` / `useRemoveDatabaseRole` with userId)
  - Group rows: Checkbox shows group-held state, clicking edits group layer (calls group variants)
- **Empty state:** "No principals with access to this database"

For **"Global"** resource:
- Same structure, but columns are global permissions (`permission:manage`, `server:manage`, etc.)
- User/group rows and effective + edit-direct logic unchanged

**Sensitive Columns** is a separate top-level tab (see below).

---

### Tab 3: Sensitive Columns

**Left pane — Column list**
- Hierarchical: servers → databases → tables → columns
- Each column row shows: table.column name, toggleable "Sensitive?" checkbox (calls `useMarkColumnSensitive` / `useUnmarkColumnSensitive`)
- Click a column to select it; matrix updates on the right

**Right pane — Bypass matrix**
- **Rows:** Principals (Users block, Groups block)
- **Columns:** Single column (the selected one)
- **Cells:** Bypass on/off checkbox (same effective/edit-direct as db-scoped roles)
- **Empty state:** "No principals with bypass for this column" or "Mark the column sensitive first"

---

## New Backend Endpoint

**`GET /api/admin/user/{userId}/effective`**

Returns a complete effective-permission view for a user, with provenance for each permission.

**Source shape (single consistent form).** Every entry in a `sources` array is the same object shape: `{ "direct": bool, "group": GroupInfo | null }`. A direct grant is `{ "direct": true, "group": null }`; a group-inherited grant is `{ "direct": false, "group": { "id": "...", "name": "..." } }`. This serializes cleanly from the C# record `EffectivePermissionSource(bool Direct, GroupInfo? Group)` and is consumed by the frontend as `Array<{ direct: boolean; group: { id: string; name: string } | null }>`. (Earlier drafts mixed bare `"direct"` strings with group objects in the same array — do **not** do that; it forces a custom converter and a discriminated union on the client.)

```json
{
  "global": [
    {
      "permission": "query:execute",
      "sources": [{ "direct": true, "group": null }]
    },
    {
      "permission": "query:audit",
      "sources": [
        { "direct": false, "group": { "id": "group-123", "name": "Engineering" } }
      ]
    },
    {
      "permission": "update:submit",
      "sources": [
        { "direct": true, "group": null },
        { "direct": false, "group": { "id": "group-456", "name": "Data Team" } }
      ]
    }
  ],
  "databaseRoles": [
    {
      "databaseId": "db-1",
      "databaseName": "prod_main",
      "serverName": "us-east-1",
      "permission": "query:execute",
      "sources": [{ "direct": true, "group": null }]
    },
    {
      "databaseId": "db-2",
      "databaseName": "staging",
      "serverName": "us-east-1",
      "permission": "query:execute",
      "sources": [
        { "direct": false, "group": { "id": "group-123", "name": "Engineering" } }
      ]
    }
  ],
  "columnBypasses": [
    {
      "databaseId": "db-1",
      "databaseName": "prod_main",
      "sensitiveColumnId": "col-1",
      "schema": "public",
      "table": "customers",
      "column": "ssn",
      "sources": [{ "direct": true, "group": null }]
    }
  ],
  "memberships": [
    { "groupId": "group-123", "groupName": "Engineering" },
    { "groupId": "group-456", "groupName": "Data Team" }
  ]
}
```

**Implementation:** Reuse patterns from `CurrentUserAccessor.cs` (group global perms), `DatabaseRoleEndpoints.cs:ListByUser` (db roles with source), and analogous logic for column bypasses. This is a read-only endpoint, mounted under the existing `/api/admin` route group (already `RequireAuthorization(Permissions.PermissionManage)`). Note `SensitiveColumn` stores `SchemaName`, `TableName`, `ColumnName` separately — surface `schema` so the column can be displayed schema-qualified.

---

## Data Model Changes

**Remove `group:manage` permission:**
- Delete from `Permissions.cs` constant and `Global` set
- Update endpoint guards (all `PermissionManage || GroupManage` → `PermissionManage`; all `userId ? … : GroupManage` → `PermissionManage`)
- Update frontend `Permission` type union and `PERMISSION_LABELS` to drop `group:manage`

**Stale data cleanup:** Permissions are stored as free strings in `user_permission` / `group_permission`, so removing the `GroupManage` *constant* leaves any existing `group:manage` *rows* behind. They become dead weight: not in the catalog, not grantable, but still present. Delete them as part of this change (a `DELETE FROM user_permission WHERE permission = 'group:manage'` plus the group table) so the data matches the model. Since this branch is unmerged and dev-only, fold the delete into the branch's regenerated migration rather than a standalone data migration.

**No schema changes expected.** Group tables, user/group permission tables, and db-role tables already exist on this branch. The effective-permissions endpoint is read-only.

**EF Migration:** If any schema touch (or the cleanup above) is needed, regenerate this branch's existing migration (squash, don't stack) per project convention — never hand-edit generated migration files.

---

## Frontend Component Structure

**New hooks (or use existing):**
- `useEffectiveUserPermissions(userId)` → fetches `/api/admin/user/{userId}/effective`
- `useUpdateUser()` — update user name/email (POST or PATCH `/api/admin/user/{userId}`)
- Existing: `useUsers()`, `useGroups()`, `useGroupMembers()`, `useGroupPermissions()`, `useDatabaseRoles()`, `useAdminServers()`, `useAssignDatabaseRole()`, `useRemoveGroupDatabaseRole()`, etc.

**Extend the existing `/access` page** — do **not** recreate it. `src/frontend/src/routes/_authed/access.tsx` already exists (~574 lines) with a three-tab `AccessPage` (`ByDatabaseTab`, `ByUserTab`, `SensitiveColumnsTab`) plus `UserRolePanel`, `DatabaseRolePanel`, `SensitiveColumnsPanel`, and is already wired to `useAssignDatabaseRole`, `useRemoveDatabaseRole`, `useMarkSensitiveColumn`/`useUnmarkSensitiveColumn`, `useGrantColumnBypass`/`useRevokeColumnBypass`, group variants (`useRemoveGroupDatabaseRole`, `useRevokeGroupColumnBypass`, `useGrantGroupPermission`/`useRevokeGroupPermission`), and membership hooks. The work is a refactor/upgrade of this file, retiring `permission.tsx` and `group.tsx` into it:
- Rename/restructure tabs to **By Principal**, **By Resource**, **Sensitive Columns**; control the active tab via `search.tab`.
- Upgrade the existing "By User" tab into **By Principal**: unify users *and* groups (segmented toggle), absorb global-permission management from `permission.tsx`, add group memberships, and render effective state + provenance from the new endpoint.
- Reuse existing hooks; the only genuinely new hooks are `useEffectiveUserPermissions` and `useUpdateUser`. Do **not** add duplicate `useMarkColumnSensitive`/`useUnmarkColumnSensitive` — the existing `useMarkSensitiveColumn`/`useUnmarkSensitiveColumn` are the canonical names.
- Reuse the existing group-creation modal already used by `group.tsx`.

**Routing:** (tab/segment carried in typed `search`, not query strings in `to`)
- `/access` with `search.tab = "principal"` (default)
- `/access` with `search.tab = "resource"`
- `/access` with `search.tab = "columns"`
- Redirects: `/permission` → `redirect({ to: "/access", search: { tab: "principal" } })`; `/group` → `redirect({ to: "/access", search: { tab: "principal", segment: "groups" } })`

---

## Effective Permission Rendering

**Visual distinction and click semantics.** A cell/row reflects *effective* state, but every click edits only the **direct** layer. There are three states:

| State | Render | Click does |
|---|---|---|
| Direct only | checked checkbox | removes the direct grant → unchecked |
| Inherited only | checked checkbox, dimmed, `[via Engineering]` badge | **adds** a direct grant (cell stays checked, now also removable) |
| Direct + inherited | checked checkbox, `[via Engineering]` badge | removes the direct grant (cell stays checked — still inherited) |

This resolves the "how do I escalate an inherited grant to direct?" question: clicking an inherited-only cell *adds* the direct grant. To fully revoke an inherited grant the admin removes the group membership (or edits the group). Tooltips clarify: "Inherited from Engineering — click to also grant directly", "Direct + inherited from Data Team — click to remove direct grant". Avoid relying on the HTML `indeterminate` visual, which is inconsistent across browsers and not clickable; use a dimmed-checked checkbox plus the badge instead.

---

## Testing

Backend tests live in `tests/IntegrationTests/` and run against the real Aspire stack (Keycloak login, XSRF, live HTTP) — there is **no** in-memory `ApiTestBase`/mock-db harness, and the suite uses plain xUnit `Assert.*`, not FluentAssertions. New endpoint tests follow the existing pattern (`GroupEndpointTests`, `AdminPermissionTests`): take `SluiceBaseStackFactory` via the constructor, sign in with `KeycloakLoginHelper`, and seed via the `Supports/*Helper` classes (`GroupTestHelper`, `PermissionTestHelper`, etc.). Entities are created through their factory methods (`User.Create`, `Group.Create`, `GroupMember.Add`, `*.Grant`) — they have private constructors, so object-initializer construction will not compile.

- Integration test for `/api/admin/user/{userId}/effective`: grant a direct permission, assert `sources` contains `{ direct: true }`.
- Integration test: add user to a group that grants `query:audit`, assert `sources` contains the group with the right name.
- Integration test: user holding a permission both directly and via group → assert two sources (one `direct: true`, one with `group`).
- Integration test: non-existent user → 404; non-`permission:manage` caller → 403.
- Frontend component tests (Vitest, in `src/frontend/.../__tests__/`): badges render for inherited grants, the three-state checkbox click model edits the direct layer only.
- E2E (Playwright): "Add user to group that grants `query:execute`" → user's effective set updates, badge shows "via GroupName".

---

## Scope & Out of Scope

**In scope:**
- Permission and Groups pages consolidation into Access with two coordinated views
- Merged admin permission (remove `group:manage`)
- Effective-permission visibility with provenance
- Group support for db-scoped roles and column bypasses in the UI
- Sensitive-column bypass UI in the unified surface

**Out of scope:**
- Performance optimization for large permission matrices (assume < 1000 principals, < 100 databases for now)
- RBAC roles beyond `permission:manage` (e.g., role templates, cross-tenant isolation)
- Audit log of permission changes (separate future work)

---

## Risks & Mitigations

1. **Permission merge's blast radius:** `permission:manage` now gates group operations too. If a `permission:manage` admin is revoked, group admins lose access.
   - *Mitigation:* Documented in release notes; use bootstrap config to re-grant on next login if needed (existing pattern). Alternative: introduce a compound permission (e.g., union of two logical permissions), but that's a larger change — out of scope.

2. **Effective-permissions endpoint performance:** Computing group-inherited perms for every user could be expensive at scale.
   - *Mitigation:* Start with the simple join approach (mirrors CurrentUserAccessor); profile and index if needed. The `/api/me` endpoint already does this, so the pattern is validated.

3. **Inherited-vs-direct click ambiguity:** A single checkbox represents effective state but edits the direct layer, which can confuse admins.
   - *Mitigation:* Use the three-state click model (dimmed-checked + badge, never the native `indeterminate` visual) defined in *Effective Permission Rendering*. Clicking an inherited-only cell adds a direct grant; full revocation of inherited grants is done by editing group membership. Tooltips state the exact effect of a click.

---

## Implementation Order

1. **Merge permissions.** Remove `group:manage` from the backend; update guards.
2. **New endpoint.** Implement `/api/admin/user/{userId}/effective` with provenance.
3. **Consolidate pages.** Build the new `/access` page with Tabs and the two views.
4. **Connect hooks.** Wire up mutations (assign/remove) to the new endpoint and existing mutations.
5. **Test & verify.** Unit tests on endpoint, integration tests on pages, E2E via browser.

---

## Success Criteria

- Single `Access` nav item; `/permission`, `/group`, `/access` all redirect.
- By Principal view shows effective global + db-scoped + bypass, with badges for inherited.
- By Resource view shows all principals (users + groups) for a database/global scope, with edit controls for direct layer.
- Sensitive Columns tab surfaces column-bypass management in the unified UI.
- No capability loss for admins who previously had split `permission:manage` + `group:manage` (merged under single `permission:manage`).
- E2E test passes: add user to group, confirm permission propagates and shows in effective view with source badge.
