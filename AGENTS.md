# AGENTS.md

## Git & GitHub

- Always develop on branches with the `feat/` prefix (e.g. `feat/add-codeowners`). Never commit directly to main.
- Keep commit messages as a single subject line — no body paragraph.
- PR descriptions use `## Summary` with bullet points only. No `## Test Plan` section.

## C# / .NET

- Suppress experimental API warnings with inline `#pragma warning disable` in the .cs file, not `<NoWarn>` in csproj.
- Never manually edit EF Core migration files. Suppress analyzer warnings via the `[**/Migrations/**.{cs,vb}]` section in `.editorconfig`.
- On an unmerged branch, regenerate the branch's own migration for further schema changes instead of stacking new ones. CI fails on `dotnet ef migrations has-pending-model-changes`.
- Abstract database-specific operations (schema introspection, connection handling) behind interfaces — never call provider drivers (Npgsql, MongoDB.Driver) directly from domain/business code.
- New top-level API routes must also be added to the YARP gateway in `src/AppHost/Program.cs`; otherwise gateway requests fall through to Vite and return 200 HTML.

## Generated Files

- Building `src/SluiceBase.Api` in Debug regenerates `src/SluiceBase.Api/openapi.json`; `npm run gen:api` (in `src/frontend`) regenerates `src/frontend/src/api/schema.ts`. Commit both — CI fails if either is stale.

## Testing

- Tests run on Microsoft.Testing.Platform: use `dotnet test --project <test project>`, with platform options after `--` (see `.github/workflows/pr-checks.yml`).
- `tests/IntegrationTests` requires a healthy Aspire stack (Postgres, Keycloak). When it can't run locally, verify with the build, unit tests, and a standalone API boot.
- Save browser screenshots under `screenshots/` (gitignored), never in the repository root.

## Code Style

- Preserve existing comments. Only remove a comment if it is factually wrong or references something that no longer exists.

## TypeScript / Frontend

- Use `Array<T>` instead of `T[]` (enforced by ESLint `@typescript-eslint/array-type`).
- Run `npm run lint` in addition to tests — ESLint enforces react-hooks rules (`refs`, `set-state-in-effect`) as errors that vitest won't catch.
