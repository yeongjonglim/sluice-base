# Recreate Update Request

**Date:** 2026-05-17

## Problem

After an Update Request is executed and fails (or is rejected/cancelled), the submitter needs to adjust the SQL or reason and resubmit. Currently they must navigate to the new request form and retype everything from scratch.

## Goal

Add a "Recreate" button to the Update Request detail page that navigates to the new request form pre-filled with the original `databaseId`, `sqlText`, and `reason`, so the submitter can adjust and resubmit with minimal friction.

## Scope

Frontend-only. No backend changes, no new API endpoints.

## Design

### Detail page — `/update/$id`

- Add a "Recreate" button to the existing action `<Group>` at the bottom of the page.
- Visible whenever `canSubmit` is true (`update:submit` permission), regardless of the request's current status.
- Clicking navigates to `/update/new?from=<id>` with no confirmation modal — it is a non-destructive navigation to a pre-filled form.

### New request form — `/update/new`

- Add a typed `from?: string` search param via TanStack Router's `validateSearch`.
- If `from` is present, call `useUpdateRequest(from)`. This is a React Query cache hit when arriving from the detail page; a network fetch otherwise (e.g. direct URL, refresh).
- Seed `databaseId`, `sqlText`, and `reason` state from the fetched request once it resolves.
- Edge case: if `from` is present but the request fails to load (deleted, no access), open the form empty — same behaviour as a fresh new request. Do not block or error the page.

### Permissions

The "Recreate" button is gated by `canSubmit` (`update:submit` permission). Users who only have `update:approve` or `update:execute` do not see it.

## Out of scope

- Any backend API changes.
- Confirmation modal before navigating.
- Handling the case where the original database no longer exists or is no longer writable — the existing submit validation on `new.tsx` already covers this (database list filters to writable only; submit is disabled until a valid database is selected).

## Testing

Add a unit test to cover the `from` search param seeding the form. Specifically verify that when `from` is present and the request loads, the `databaseId`, `sqlText`, and `reason` fields are initialised with the original request's values.
