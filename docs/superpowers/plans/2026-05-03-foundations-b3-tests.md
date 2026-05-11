# Foundations B3 — Postgres engine tests + Playwright E2E

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** `docs/superpowers/specs/2026-05-03-foundations-design.md` — sub-slice B3 (§9.3).

**Goal:** Add the two `PostgresTargetEngine` integration tests that the existing `Aspire.Hosting.Testing` harness now makes possible, wire Playwright with one happy-path login E2E, and document the test pyramid so later sub-projects know where new tests belong.

**Scope reduction vs spec:** The spec lists "migrate from `WebApplicationFactory<Program>` to `Aspire.Hosting.Testing` `AppHostFixture`" as the first B3 deliverable. **That work is already done** — the user has it landed on `feat/init` as `tests/IntegrationTests/Supports/SluiceBaseStackFactory.cs` (xUnit `IClassFixture`/`ICollectionFixture` against a real Aspire-booted app). This plan **excludes** that migration and only covers what remains.

**Architecture:** Tests for `PostgresTargetEngine` instantiate the engine in-process and feed it connection strings retrieved from the live Aspire app (`factory.InitialisedApp.GetConnectionStringAsync("blue-appdb")`). To do this without making `PostgresTargetEngine` `public`, we add an `InternalsVisibleTo` to `SluiceBase.Api`. Playwright runs out-of-process against the same Aspire-served frontend and Keycloak — `webServer: undefined`, expects `aspire run` already active.

**Tech Stack:** xUnit (already wired) + Aspire.Hosting.Testing (already wired) for backend; `@playwright/test` (chromium) for E2E.

---

## Pre-flight: state after B2

Verified at plan-write time:

- `tests/IntegrationTests/IntegrationTests.csproj` references `AppHost.csproj` and `SluiceBase.Api.csproj`, uses `Aspire.Hosting.Testing` 13.2.4 + xUnit 2.9.3.
- `tests/IntegrationTests/Supports/SluiceBaseStackFactory.cs` is the shared collection fixture (`[Collection("Aspire")]`). It boots `DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>` with `DcpPublisher:RandomizePorts=false` (fixed ports for OIDC redirects), disables `AllowAutoRedirect` on every HTTP client, and applies a `MakeTransient()` extension that strips volume mounts / persistent container lifetimes / pgAdmin so test runs don't pollute the real dev DB.
- `tests/IntegrationTests/Supports/Extensions/DistributedApplicationTestingBuilderExtensions.cs` defines `MakeTransient()` and adds resilient HTTP defaults.
- Existing tests: `HealthEndpointsTests` (2), `AuthEndpointsTests` (2), `OpenApiTests` (1) — all use `factory.InitialisedApp.CreateHttpClient("api", "https")`.
- `SluiceBase.Api.Targets.PostgresTargetEngine` is `internal sealed` and parameterless.
- AppHost defines `target-blue-pg` with database resource named `blue-appdb` (alias `appdb`), bind-mounted seed at `seed/blue/`. So `factory.InitialisedApp.GetConnectionStringAsync("blue-appdb")` is the right call to obtain a working connection string.
- Frontend has Vitest config + `@testing-library/*` already; Playwright is **not** yet present in `package.json`.

---

## File structure

**Files created:**

| Path | Responsibility |
|---|---|
| `tests/IntegrationTests/TargetEngineTests.cs` | Two xUnit tests for `PostgresTargetEngine`. |
| `src/frontend/playwright.config.ts` | Playwright config (chromium, no auto-start, baseURL = local frontend). |
| `src/frontend/e2e/login.spec.ts` | Happy-path login + logout E2E. |
| `src/frontend/.gitignore` | Add `test-results/`, `playwright-report/`, `blob-report/`, `playwright/.cache/` if not already global. |
| `docs/TESTING.md` | Test pyramid: when to write what (xUnit integration vs Vitest unit vs Playwright E2E). |

**Files modified:**

| Path | What changes |
|---|---|
| `src/SluiceBase.Api/SluiceBase.Api.csproj` | Add `<InternalsVisibleTo Include="IntegrationTests" />` so tests can reference `PostgresTargetEngine` without making it public. |
| `src/frontend/package.json` | Add `@playwright/test` to devDependencies; add `test:e2e` and `test:e2e:ui` scripts. |
| `src/frontend/README.md` | Add a short "End-to-end testing" section pointing at `docs/TESTING.md` for detail. |

---

## Task 0: Branch setup

**Files:** none.

- [ ] **Step 1: Verify clean working tree on `feat/init`**

```bash
git status --short
```

Expected: empty.

- [ ] **Step 2: Create the worktree**

```bash
git worktree add -b feat/foundations-b3-tests ../sluice-base.b3
```

Expected: `Preparing worktree (new branch 'feat/foundations-b3-tests')`. From here on, all paths are inside `/Users/voltendron/Projects/sluice-base.b3`.

---

## Task 1: Expose `SluiceBase.Api` internals to the test project

**Files:**
- Modify: `src/SluiceBase.Api/SluiceBase.Api.csproj`

- [ ] **Step 1: Add `InternalsVisibleTo`**

Open `src/SluiceBase.Api/SluiceBase.Api.csproj`. After the existing `<ItemGroup>` containing `<ProjectReference>` entries (or append a new `<ItemGroup>`), add:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="IntegrationTests" />
</ItemGroup>
```

This is the SDK-style equivalent of the legacy `[assembly: InternalsVisibleTo("...")]` attribute and keeps the metadata visible in the project file (where dependencies belong) rather than buried in a code file.

The project's existing top-level structure should now be approximately:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>...</PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\SluiceBase.Core\SluiceBase.Core.csproj" />
    <ProjectReference Include="..\ServiceDefaults\ServiceDefaults.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="IntegrationTests" />
  </ItemGroup>
  <ItemGroup>
    <!-- existing PackageReference list -->
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Verify build is clean**

```bash
dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base.b3
git add src/SluiceBase.Api/SluiceBase.Api.csproj
git commit -m "chore(api): expose internals to IntegrationTests"
```

---

## Task 2: `TargetEngineTests.cs`

**Files:**
- Create: `tests/IntegrationTests/TargetEngineTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/IntegrationTests/TargetEngineTests.cs`:

```csharp
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using SluiceBase.Api.Targets;
using SluiceBase.Core.Targets;

namespace IntegrationTests;

[Collection("Aspire")]
public sealed class TargetEngineTests(SluiceBaseStackFactory factory)
{
    [Fact]
    public async Task TargetEngine_Postgres_TestConnection_Succeeds()
    {
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", TestContext.Current.CancellationToken);

        Assert.NotNull(connectionString);

        ITargetEngine engine = new PostgresTargetEngine();
        var result = await engine.TestConnectionAsync(
            connectionString,
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok, result.Error);
        Assert.Null(result.Error);
        Assert.Equal("postgres", engine.Kind);
    }

    [Fact]
    public async Task TargetEngine_Postgres_TestConnection_Fails_OnBadConnString()
    {
        const string brokenConnectionString =
            "Host=does-not-exist.invalid;Port=65000;Database=appdb;Username=u;Password=p;Timeout=2";

        ITargetEngine engine = new PostgresTargetEngine();
        var result = await engine.TestConnectionAsync(
            brokenConnectionString,
            TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }
}
```

Notes:
- `[Collection("Aspire")]` reuses the existing `SluiceBaseStackFactory` collection, so the AppHost is booted once for the whole test run, not per class.
- `factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct)` returns the connection string Aspire generated for the `target-blue-pg` Postgres resource (host, port, ephemeral container password). Casts to non-null after the assertion.
- `PostgresTargetEngine` is reachable because Task 1 added `InternalsVisibleTo`. The test references it via `using SluiceBase.Api.Targets;`.
- The bad-connection-string test uses `does-not-exist.invalid` (TLD reserved by RFC 6761 — guaranteed not to resolve) plus `Timeout=2` so the test doesn't dawdle.
- `TestContext.Current.CancellationToken` is xUnit v3 / 2.9+'s test-scoped cancellation token. If the running xUnit version is older, replace with `TestContext.Current.CancellationToken` → `CancellationToken.None`.

- [ ] **Step 2: Run the new tests**

```bash
dotnet test tests/IntegrationTests --filter "FullyQualifiedName~TargetEngine"
```

Expected: both tests pass (`- Failed: 0, Passed: 2`).

The first test takes ~5-15s on cold start (Aspire boots the Postgres container if no other test already triggered it in the same run); the second is fast (just an Npgsql open against a guaranteed-unresolvable host that fails inside the timeout).

If the first test fails with an EF / migration error, recheck that the `target-blue-pg` resource is healthy in the booted app — `MakeTransient()` strips volume mounts but leaves the container itself.

- [ ] **Step 3: Run the full integration test suite**

```bash
dotnet test tests/IntegrationTests
```

Expected: 7 tests pass (5 pre-existing + 2 new).

- [ ] **Step 4: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base.b3
git add tests/IntegrationTests/TargetEngineTests.cs
git commit -m "test(api): assert PostgresTargetEngine TestConnectionAsync behaviour"
```

---

## Task 3: Add Playwright to the frontend

**Files:**
- Modify: `src/frontend/package.json`

- [ ] **Step 1: Add `@playwright/test` to devDependencies**

From `src/frontend`:

```bash
npm install --save-dev @playwright/test@^1.50.0
```

(Use the latest stable 1.x — `npm install` will pick the highest matching the `^1.50.0` floor.)

Expected: `package-lock.json` is updated.

- [ ] **Step 2: Add E2E scripts to `package.json`**

Open `src/frontend/package.json`. Inside `"scripts"`, add two entries (after `test`):

```json
"test:e2e": "playwright test",
"test:e2e:ui": "playwright test --ui"
```

The full `"scripts"` block should now look like:

```json
"scripts": {
  "dev": "vite",
  "gen:api": "openapi-typescript ../SluiceBase.Api/openapi.json -o src/api/schema.ts",
  "prebuild": "npm run gen:api",
  "build": "tsc -b && vite build",
  "lint": "eslint .",
  "preview": "vite preview",
  "test": "vitest run",
  "test:e2e": "playwright test",
  "test:e2e:ui": "playwright test --ui"
}
```

- [ ] **Step 3: Install the Playwright chromium browser binary**

```bash
npx playwright install chromium
```

This downloads the chromium binary into Playwright's per-user cache (`~/Library/Caches/ms-playwright` on macOS). Not in the repo — only the package and config are committed.

Expected output ends with something like `Chromium ... downloaded to ...`.

- [ ] **Step 4: Update `.gitignore` so Playwright outputs aren't accidentally committed**

Open `src/frontend/.gitignore` (create if it doesn't exist; otherwise append). Add:

```
# Playwright
test-results/
playwright-report/
blob-report/
playwright/.cache/
```

If the repo's root `.gitignore` already excludes these globally, the per-frontend file is unnecessary — verify with `cat ../../.gitignore | grep -i playwright`. If covered globally, skip creating the file.

- [ ] **Step 5: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base.b3
git add src/frontend/package.json src/frontend/package-lock.json
# Add .gitignore only if you created/modified one in step 4
git add src/frontend/.gitignore 2>/dev/null || true
git commit -m "chore(frontend): add @playwright/test for E2E"
```

---

## Task 4: `playwright.config.ts`

**Files:**
- Create: `src/frontend/playwright.config.ts`

- [ ] **Step 1: Write the config**

Create `src/frontend/playwright.config.ts`:

```ts
import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  reporter: process.env.CI ? "github" : "list",
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:5173",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
    ignoreHTTPSErrors: true,
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
});
```

Notes:
- `webServer` is **deliberately not set**. E2E expects `aspire run` to already be running. Auto-starting Aspire from Playwright is fragile (port collisions, slow boot, container side effects).
- `workers: 1` — the SluiceBase realm has shared state (Keycloak users, sessions) and parallel logins as the same user produce races. One worker keeps tests honest until we have per-test isolation.
- `fullyParallel: false` for the same reason.
- `ignoreHTTPSErrors: true` is for the rare path where someone runs the SPA directly against the api over HTTPS without the Vite proxy.
- `baseURL` defaults to the Vite dev port (5173). Override with `E2E_BASE_URL` env var if needed.

- [ ] **Step 2: Commit (deferred — combined with Task 5)**

---

## Task 5: `e2e/login.spec.ts`

**Files:**
- Create: `src/frontend/e2e/login.spec.ts`

- [ ] **Step 1: Write the happy-path E2E**

Create `src/frontend/e2e/login.spec.ts`:

```ts
import { expect, test } from "@playwright/test";

test.describe("BFF login flow", () => {
  test("alice can log in, see the shell, and log out", async ({ page }) => {
    // 1. Land on the SPA. Auth bootstrap should redirect us to Keycloak.
    await page.goto("/");

    await expect(page).toHaveURL(
      /\/realms\/sluicebase\/protocol\/openid-connect\/auth/,
      { timeout: 15_000 },
    );

    // 2. Sign in as alice.
    await page.getByLabel(/username/i).fill("alice");
    await page.getByLabel(/password/i).fill("dev");
    await page.getByRole("button", { name: /sign in/i }).click();

    // 3. Land back on the SPA's authed shell.
    await expect(page).toHaveURL(/^http:\/\/localhost:5173\/?$/, {
      timeout: 15_000,
    });
    await expect(page.getByRole("heading", { level: 2 })).toContainText(
      /Welcome,/i,
    );
    await expect(page.getByRole("heading", { level: 2 })).toContainText(
      /alice/i,
    );

    // 4. Open the user menu and log out.
    await page.getByRole("button", { name: /user menu/i }).click();
    await page.getByRole("menuitem", { name: /log out/i }).click();

    // 5. After logout, landing on the SPA should redirect us back to Keycloak login.
    await page.goto("/");
    await expect(page).toHaveURL(
      /\/realms\/sluicebase\/protocol\/openid-connect\/auth/,
      { timeout: 15_000 },
    );
  });
});
```

Notes on selectors:
- `getByLabel(/username/i)` and `getByLabel(/password/i)` match Keycloak's default form labels. If Keycloak's English labels differ (`Username or email` etc.), the regex's `/username/i` still matches.
- `getByRole("button", { name: /sign in/i })` matches Keycloak's submit button.
- `getByRole("button", { name: /user menu/i })` relies on the `aria-label="User menu"` set on the `ActionIcon` in `_authed.tsx` (Task 9 of B2). If the user menu's accessible name differs in the merged code, update the selector.
- The "Welcome, alice" assertion treats `alice` (preferred_username) as the displayed name; if the seeded realm gives Alice a `firstName: "Alice"` so the name claim populates, the regex `/alice/i` still matches case-insensitively.

- [ ] **Step 2: Run the E2E (with Aspire running)**

In a separate terminal, start the app:

```bash
aspire run
```

Wait for all six resources Healthy. Then:

```bash
cd src/frontend
npm run test:e2e
```

Expected: 1 test passed.

If the test fails, the most useful debug step is `npm run test:e2e:ui` which opens Playwright's UI mode for stepwise inspection.

Common failures:
- **Click on Keycloak's "Sign in" button does nothing:** the form might be inside an `<iframe>` or the button might be `<input type="submit">` rather than `<button>`. Adjust selector.
- **Welcome heading not found:** `_authed/index.tsx` displays the user's name from `me.data.name ?? me.data.preferredUsername ?? me.data.email`. The realm seeds Alice with `firstName: "Alice", lastName: "Dev"` — the resulting `name` claim depends on Keycloak's mapper. If `name` is null, fall through to `preferredUsername` ("alice") which the regex `/alice/i` still matches.
- **Logout doesn't return to Keycloak login:** the `<a href="/logout">` should trigger the BFF logout chain. Verify the route map and the Vite proxy entry.

- [ ] **Step 3: Stop Aspire**

`Ctrl+C` in the `aspire run` terminal.

- [ ] **Step 4: Commit (combined with Task 4)**

```bash
cd /Users/voltendron/Projects/sluice-base.b3
git add src/frontend/playwright.config.ts src/frontend/e2e/login.spec.ts
git commit -m "test(frontend): playwright config + happy-path login E2E"
```

---

## Task 6: `docs/TESTING.md` — test pyramid

**Files:**
- Create: `docs/TESTING.md`

- [ ] **Step 1: Write the doc**

Create `docs/TESTING.md`:

```markdown
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
```

- [ ] **Step 2: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base.b3
git add docs/TESTING.md
git commit -m "docs: add test pyramid and conventions"
```

---

## Task 7: README pointer to `docs/TESTING.md`

**Files:**
- Modify: `src/frontend/README.md`

- [ ] **Step 1: Add a short section near the existing "Scripts" block**

Append the following to `src/frontend/README.md` (after the existing "Scripts" or "Layout" section, whichever comes last):

```markdown
## End-to-end testing

Playwright E2E lives in `e2e/` and assumes `aspire run` is already active. From this directory:

```bash
npm run test:e2e        # headless run
npm run test:e2e:ui     # interactive Playwright UI
```

See [docs/TESTING.md](../../docs/TESTING.md) for the full test pyramid and conventions.
```

- [ ] **Step 2: Commit**

```bash
cd /Users/voltendron/Projects/sluice-base.b3
git add src/frontend/README.md
git commit -m "docs(frontend): point README at TESTING.md"
```

---

## Task 8: Final acceptance

**Files:** none.

- [ ] **Step 1: Full clean build**

From the repo root:

```bash
dotnet build SluiceBase.slnx
cd src/frontend && npm run build && cd ../..
```

Expected: both clean, zero warnings.

- [ ] **Step 2: Full test suite (no E2E yet)**

```bash
dotnet test SluiceBase.slnx
cd src/frontend && npm run test && cd ../..
```

Expected:
- `dotnet test`: 7 tests pass (5 pre-existing + 2 `TargetEngineTests`).
- `npm run test`: 4 vitest tests pass.

- [ ] **Step 3: Playwright E2E with Aspire active**

In one terminal:

```bash
aspire run
```

In another:

```bash
cd src/frontend && npm run test:e2e
```

Expected: 1 test passed.

Stop Aspire with `Ctrl+C`.

- [ ] **Step 4: No commit**

If everything passes, B3 is done. If a fix was required during smoke, commit each as its own focused commit.

---

## Acceptance criteria recap (from spec §9.3, scope-reduced)

- [x] `dotnet test` passes against Aspire-booted Postgres for `TargetEngine_Postgres_*` — Task 2.
- [x] `npm run test:e2e` (with `aspire run` active) passes the login happy-path spec — Task 5.
- [x] All B1 + B2 acceptance criteria still hold — Task 8 step 2.
- [x] Test pyramid documented — Task 6.

(The "migrate to `Aspire.Hosting.Testing` `AppHostFixture`" deliverable is excluded — already complete on `feat/init` as `SluiceBaseStackFactory`.)

---

## Self-review notes

- **Spec coverage:** §9.3 deliverables, scope-reduced:
  - "Migrate from `WebApplicationFactory` to `Aspire.Hosting.Testing`" — already done, excluded.
  - "`TargetEngine_Postgres_*` tests added" — Task 2.
  - "`frontend/playwright.config.ts` + `e2e/login.spec.ts`" — Tasks 4 + 5.
  - "`npm run test:e2e` script" — Task 3.
  - "README documents: aspire run first, then npm run test:e2e" — Task 7.
  - "Test pyramid explanation, when to write what" — Task 6.
- **Placeholder scan:** No "TBD" / "TODO" / "fill in later" in the plan body.
- **Type / API consistency:**
  - `SluiceBaseStackFactory` and `[Collection("Aspire")]` (Task 2) match the user's existing `tests/IntegrationTests/Supports/SluiceBaseStackFactory.cs` and its `SluiceBaseCollectionDefinition`.
  - `factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ...)` — name matches `AppHost.cs`'s `.AddDatabase("blue-appdb", "appdb")`.
  - `PostgresTargetEngine` reachable via `using SluiceBase.Api.Targets;` once Task 1's `InternalsVisibleTo` lands.
  - Selectors in `login.spec.ts` (`aria-label="User menu"`, "Log out" menu item) match the `_authed.tsx` shell from B2.
- **Known wrinkle:** `TestContext.Current.CancellationToken` requires xUnit v3 or 2.9+; if the project's xUnit version is older, swap to `CancellationToken.None`. The `IntegrationTests.csproj` shows xUnit 2.9.3 — supported.
