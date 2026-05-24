# CLAUDE.md

## Git & GitHub

- Always develop on branches with the `feat/` prefix (e.g. `feat/add-codeowners`). Never commit directly to main.
- Keep commit messages as a single subject line — no body paragraph.
- PR descriptions use `## Summary` with bullet points only. No `## Test Plan` section.

## C# / .NET

- Suppress experimental API warnings with inline `#pragma warning disable` in the .cs file, not `<NoWarn>` in csproj.
- Never manually edit EF Core migration files. Suppress analyzer warnings via the `[**/Migrations/**.{cs,vb}]` section in `.editorconfig`.
- Abstract database-specific operations (schema introspection, connection handling) behind interfaces — never hard-code Npgsql calls in domain/business code.

## TypeScript / Frontend

- Use `Array<T>` instead of `T[]` (enforced by ESLint `@typescript-eslint/array-type`).
