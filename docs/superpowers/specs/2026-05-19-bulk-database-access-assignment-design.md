# Bulk Database Access Assignment

**Issue:** #34  
**Date:** 2026-05-19  
**Status:** Approved

## Problem

The Access control page (`/access`) currently requires a modal interaction per assignment: select user, select database, select permission, click Assign — one row at a time. Onboarding a new user across several databases requires dozens of modal interactions.

## Goal

Replace the modal-based assignment flow with an inline checkbox matrix so admins can view and toggle any assignment in a single interaction, with no modals.

## Scope

Frontend only — `src/frontend/src/routes/_authed/access.tsx`. No new API endpoints, no backend changes, no new routes.

## Design

### What changes and what stays

The tab structure (By Database / By User), sidebar, hooks, and all API endpoints remain unchanged. Only the right-hand panel rendered after selecting a user or database is replaced.

### By User tab — `UserRolePanel`

When a user is selected, render a matrix table:

- **Rows:** all databases across all servers, grouped under server-name subheadings
- **Columns:** the five scopeable permissions (`query:execute`, `query:audit`, `update:submit`, `update:approve`, `update:execute`)
- **Cells:** checkboxes — checked if the `(databaseId, permission)` pair exists in the user's current roles
- **On check:** call `assignUserRole.mutate({ userId, databaseId, permission })`
- **On uncheck:** call `removeDatabaseRole.mutate({ databaseId, userId, permission })`

Disabled databases are already excluded from the sidebar and will not appear as rows.

### By Database tab — `DatabaseRolePanel`

Same pattern with axes flipped:

- **Rows:** all users (fetched via existing `useUsers`)
- **Columns:** same five permissions
- **Cells:** checkboxes against the database's current role list

### Hooks used (no new hooks needed)

| Hook | Used by |
|---|---|
| `useUserRoles(userId)` | `UserRolePanel` — current assignments |
| `useAssignUserRole` | `UserRolePanel` — check |
| `useDatabaseRoles(databaseId)` | `DatabaseRolePanel` — current assignments |
| `useAssignDatabaseRole` | `DatabaseRolePanel` — check |
| `useRemoveDatabaseRole` | Both panels — uncheck |
| `useUsers` | `DatabaseRolePanel` — row list |
| `useAdminServers` | `UserRolePanel` — row list grouped by server |

### Interaction model

- Every checkbox stays interactive at all times — no per-cell disabling while mutations are in-flight.
- Each toggle fires its mutation immediately.
- A local in-flight counter tracks concurrent mutations across the whole matrix. When the counter returns to zero after having been above zero, and all completed successfully, show a `notifications.show(...)` success toast ("Access updated").
- If any mutation fails, follow existing error handling behaviour (no additional toast).

### Loading state

While `roles.isLoading` is true on initial fetch, all checkboxes render as `disabled` until data arrives.

### Empty states

- No databases configured: short "No databases configured" message in the panel.
- No users: keep existing "No assignments yet" text.

## Out of scope

- Backend bulk-assign endpoint (not needed — individual toggles are fine)
- New routes or pages
- Changes to the global Permissions page (`/permission`)
- Changes to any hooks or API client
