# Testing in SluiceBase

SluiceBase uses three test layers. When adding a feature, write the test that lives at the lowest layer it can confidently cover.

## Layers

### Vitest unit tests — `src/frontend/src/**/*.test.{ts,tsx}`

For **frontend logic** that is testable in isolation: the `fetch` wrapper, pure utilities, hook behaviour against mocked responses, formatters.

- Environment: `jsdom`.
- Test runner: `vitest`.
- Mock: `vi.stubGlobal("fetch", ...)` for HTTP, `@testing-library/react` for component rendering, `@testing-library/jest-dom` matchers.
- Fast (sub-second per file). Run them often.

```bash
cd src/frontend && npm run test
```

### xUnit integration tests — `tests/IntegrationTests/**/*Tests.cs`

For **backend behaviour** that needs the real ASP.NET pipeline, EF, Keycloak, or Postgres. The shared `SluiceBaseStackFactory` boots a transient Aspire `DistributedApplication` once per test run (`[Collection("Aspire")]`) and tests obtain HTTP clients via `factory.InitialisedApp.CreateHttpClient("api", "https")` and connection strings via `factory.InitialisedApp.GetConnectionStringAsync(...)`.

- Auto-redirect is **disabled** on every HTTP client so 302/401 are observable.
- Container volumes and persistent lifetimes are stripped (`MakeTransient()` extension) so test runs are isolated.
- Slow (~10-30s for the boot, then milliseconds per test). One full pyramid run per CI build is fine.

```bash
dotnet test tests/IntegrationTests
```

### Playwright E2E — `src/frontend/e2e/**/*.spec.ts`

For **flows that span both halves** and depend on real browser behaviour: the BFF login round-trip, navigation, redirect chains, cookie handling.

- Single project: `chromium`.
- `webServer` is intentionally not configured; **`aspire run` must be active** before running E2E.
- One worker, not parallel — until per-test isolation is in place.

```bash
# Terminal 1
aspire run
# Terminal 2
cd src/frontend && npm run test:e2e
# or interactively:
cd src/frontend && npm run test:e2e:ui
```

## Where does my new test belong?

| Question | Answer |
|---|---|
| Is the behaviour pure-frontend logic? | Vitest. |
| Does the assertion need the real ASP.NET pipeline, a real DB, or Keycloak's discovery? | xUnit integration. |
| Is the assertion about *what the user sees in the browser* or a flow that crosses backend + frontend? | Playwright. |
| Could you write it as both a Vitest unit and a Playwright E2E? | Vitest. Playwright is expensive — reserve it for what only it can cover. |

## Conventions

- **Name files for the unit, not the test type.** `client.test.ts` (the unit), not `client.unit.test.ts` (the type). The directory path already disambiguates.
- **One concern per `[Fact]` / `it()`.** A test that asserts both 401-not-302 and the response body is two tests in a trench coat — split it.
- **Don't mock what you can use directly.** Real Postgres via Aspire beats InMemory EF. Real Keycloak via Aspire beats hand-rolled JWT issuance. Reach for mocks only when the dependency makes the test slow, flaky, or destructive.
- **Tests live next to the code they test on the frontend** (`src/api/__tests__/client.test.ts`). On the backend they live in `tests/IntegrationTests/` because integration tests don't share an assembly with their target.
  ``
