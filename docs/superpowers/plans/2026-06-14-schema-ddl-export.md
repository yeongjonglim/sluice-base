# Schema DDL Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user with `query:execute` on a database download its schema as `pg_dump`-equivalent DDL (`.sql`) from the Diagram page, for diffing the live schema against codebase migrations.

**Architecture:** A new `ITargetEngine.ExportSchemaDdlAsync` method whose Postgres implementation shells out to the real `pg_dump --schema-only --no-owner --no-privileges` (password via `PGPASSWORD`, arg-list, async pipe reads). A new `GET /api/schema/{databaseId}/ddl` endpoint reuses the existing schema-view authorization and returns the DDL as a file. The frontend adds an "Export DDL" button to `/query/diagram` that fetches the text and saves it client-side. `--schema-only` is hard-coded server-side so no data can ever be emitted — the data-protection invariant holds by construction.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, Npgsql, `pg_dump`; React + TypeScript, Mantine, TanStack Query; Aspire (integration tests), Vitest (frontend).

**Spec:** `docs/superpowers/specs/2026-06-14-schema-ddl-export-design.md`

**Branch:** `feat/schema-ddl-export` (already checked out)

---

## Background notes for the implementer

- **`pg_dump` must be on PATH wherever the API process runs** (the runtime container, the CI test runner, and the local dev machine). Integration tests in `tests/IntegrationTests` invoke `pg_dump` on the *test host*, not in a container.
- **Version rule:** `pg_dump` refuses to dump from a server whose major version is **newer** than the client; a client equal-or-newer than the server always works. Task 1 pins the target Postgres containers to major **17**; Tasks 2 install a `pg_dump` ≥ 17 in the Dockerfile and CI.
- **Per the project's local-OIDC caveat,** the full integration-test suite may not run on a local macOS/podman machine. Where a step says "run integration tests", verify the code **compiles** locally (`dotnet build`) and rely on CI for green tests. Do not block the plan on local integration-test execution.
- **OpenAPI is generated at build:** `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj` regenerates `src/SluiceBase.Api/openapi.json` (via `Microsoft.Extensions.ApiDescription.Server`). CI fails if it is out of date. The frontend `npm run gen:api` regenerates `src/frontend/src/api/schema.ts` from that file.

---

## Task 1: Pin target Postgres image tags

Deterministically fix the dumped servers' major version so the `pg_dump` client version can be matched.

**Files:**
- Modify: `src/AppHost/Program.cs`

- [ ] **Step 1: Pin the blue target Postgres image tag**

In `src/AppHost/Program.cs`, find the `target-blue-pg` registration and add `.WithImageTag("17")`. The block becomes:

```csharp
var blueDbInstance = builder.AddPostgres("target-blue-pg")
    .WithImageTag("17")
    // Set the name of the default database to auto-create on container startup.
    .WithEnvironment("POSTGRES_DB", appDbName)
    // Mount the SQL scripts directory into the container so that the init scripts run.
    .WithBindMount("seed/blue", "/docker-entrypoint-initdb.d")
    .WithHostPort(55532)
    .WithDataVolume();
```

- [ ] **Step 2: Pin the green target Postgres image tag**

Apply the same `.WithImageTag("17")` to the `target-green-pg` registration:

```csharp
var greenDbInstance = builder.AddPostgres("target-green-pg")
    .WithImageTag("17")
    // Set the name of the default database to auto-create on container startup.
    .WithEnvironment("POSTGRES_DB", appDbName)
    // Mount the SQL scripts directory into the container so that the init scripts run.
    .WithBindMount("seed/green", "/docker-entrypoint-initdb.d")
    .WithHostPort(55533)
    .WithDataVolume();
```

- [ ] **Step 3: Build the AppHost to verify it compiles**

Run: `dotnet build src/AppHost/AppHost.csproj --configuration Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/AppHost/Program.cs
git commit -m "chore: pin target postgres images to major 17"
```

---

## Task 2: Provide pg_dump in the Docker runtime image and CI

Install a `pg_dump` (≥ major 17) wherever the API and the integration tests run.

**Files:**
- Modify: `Dockerfile`
- Modify: `.github/workflows/pr-checks.yml`

- [ ] **Step 1: Add postgresql-client to the runtime image**

In `Dockerfile`, change the runtime stage's `apk add` line to also install the Postgres client (Alpine's `postgresql17-client` package matches major 17). Replace:

```dockerfile
RUN apk add --no-cache krb5-libs
```

with:

```dockerfile
# postgresql17-client provides pg_dump for the schema DDL export feature.
# Keep the major version >= the Postgres servers being dumped (see AppHost image tags).
RUN apk add --no-cache krb5-libs postgresql17-client
```

- [ ] **Step 2: Verify the Docker image builds and pg_dump is present**

Run: `docker build -t sluicebase-ddl-check . && docker run --rm --entrypoint pg_dump sluicebase-ddl-check --version`
Expected: Image builds; prints `pg_dump (PostgreSQL) 17.x`.

(If Docker is unavailable locally, skip execution — CI's image build covers it. Do not block.)

- [ ] **Step 3: Install pg_dump 17 in the CI backend job**

In `.github/workflows/pr-checks.yml`, in the `backend` job, add a step that installs the PostgreSQL 17 client from the PGDG apt repo **before** the "Run integration tests" step. Insert it immediately after the "Build" step:

```yaml
      - name: Install PostgreSQL 17 client (pg_dump)
        run: |
          sudo install -d /usr/share/postgresql-common/pgdg
          sudo curl -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc \
            --fail https://www.postgresql.org/media/keys/ACCC4CF8.asc
          . /etc/os-release
          echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt ${VERSION_CODENAME}-pgdg main" \
            | sudo tee /etc/apt/sources.list.d/pgdg.list
          sudo apt-get update
          sudo apt-get install -y postgresql-client-17
          echo "/usr/lib/postgresql/17/bin" >> "$GITHUB_PATH"
          /usr/lib/postgresql/17/bin/pg_dump --version
```

- [ ] **Step 4: Commit**

```bash
git add Dockerfile .github/workflows/pr-checks.yml
git commit -m "build: install pg_dump 17 in runtime image and CI"
```

---

## Task 3: Add `ExportSchemaDdlAsync` to the target engine

**Files:**
- Modify: `src/SluiceBase.Core/Targets/ITargetEngine.cs`
- Modify: `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`
- Test: `tests/IntegrationTests/TargetEngineTests.cs`

- [ ] **Step 1: Write the failing engine test**

Add this test to `tests/IntegrationTests/TargetEngineTests.cs` (inside the `TargetEngineTests` class):

```csharp
    [Fact]
    public async Task TargetEngine_Postgres_ExportSchemaDdl_ReturnsSchemaOnlyDdl()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", ct);

        Assert.NotNull(connectionString);

        var ddl = await _targetEngine.ExportSchemaDdlAsync(connectionString, ct);

        // Structure is present...
        Assert.Contains("CREATE TABLE", ddl);
        Assert.Contains("users", ddl);
        // ...but no data is ever emitted (data-protection invariant).
        Assert.DoesNotContain("COPY ", ddl);
        Assert.DoesNotContain("INSERT INTO", ddl);
    }
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet build tests/IntegrationTests/IntegrationTests.csproj --configuration Debug`
Expected: FAIL — `'ITargetEngine' does not contain a definition for 'ExportSchemaDdlAsync'`.

- [ ] **Step 3: Add the interface method**

In `src/SluiceBase.Core/Targets/ITargetEngine.cs`, add this method to the `ITargetEngine` interface (after `GetSchemaAsync`):

```csharp
    Task<string> ExportSchemaDdlAsync(
        string connectionString,
        CancellationToken ct);
```

- [ ] **Step 4: Implement it in PostgresTargetEngine**

In `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`, add `using System.Diagnostics;` to the top with the other `using` directives, then add this method to the `PostgresTargetEngine` class (e.g. after `GetSchemaAsync`):

```csharp
    public async Task<string> ExportSchemaDdlAsync(string connectionString, CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        var psi = new ProcessStartInfo
        {
            FileName = "pg_dump",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // --schema-only is hard-coded and non-overridable: this method exposes no parameter
        // that could request table data, so the data-protection invariant holds by construction.
        // --no-owner / --no-privileges keep diffs against codebase migrations clean.
        psi.ArgumentList.Add("--schema-only");
        psi.ArgumentList.Add("--no-owner");
        psi.ArgumentList.Add("--no-privileges");
        psi.ArgumentList.Add($"--host={builder.Host}");
        psi.ArgumentList.Add($"--port={builder.Port}");
        psi.ArgumentList.Add($"--username={builder.Username}");
        psi.ArgumentList.Add($"--dbname={builder.Database}");

        // Password is passed only via the child process environment — never on the command line.
        psi.Environment["PGPASSWORD"] = builder.Password ?? string.Empty;

        using var process = new Process { StartInfo = psi };
        process.Start();

        // Read both pipes concurrently to avoid a full-buffer deadlock.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"pg_dump exited with code {process.ExitCode}: {stderr}");
        }

        return stdout;
    }
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build SluiceBase.slnx --configuration Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Run the engine test (CI executes it; locally only if your stack runs)**

Run: `dotnet test tests/IntegrationTests --configuration Debug --filter "FullyQualifiedName~ExportSchemaDdl"`
Expected: PASS. If the Aspire stack does not start locally (podman/macOS), confirm the build succeeded in Step 5 and rely on CI.

- [ ] **Step 7: Commit**

```bash
git add src/SluiceBase.Core/Targets/ITargetEngine.cs src/SluiceBase.Api/Targets/PostgresTargetEngine.cs tests/IntegrationTests/TargetEngineTests.cs
git commit -m "feat: add pg_dump-based schema DDL export to target engine"
```

---

## Task 4: Add the `GET /api/schema/{databaseId}/ddl` endpoint

**Files:**
- Modify: `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs`
- Test: `tests/IntegrationTests/SchemaEndpointTests.cs`
- Regenerate: `src/SluiceBase.Api/openapi.json`

- [ ] **Step 1: Write the failing endpoint tests**

Add these tests to `tests/IntegrationTests/SchemaEndpointTests.cs` (inside the `SchemaEndpointTests` class, alongside the existing `GetSchema_*` tests):

```csharp
    [Fact]
    public async Task ExportDdl_ReturnsSchemaOnlyDdl_ForBlueDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;

        var resp = await session.Client.GetAsync($"/api/schema/{databaseId}/ddl", ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var ddl = await resp.Content.ReadAsStringAsync(ct);
        Assert.Contains("CREATE TABLE", ddl);
        Assert.Contains("users", ddl);
        // Data-protection invariant: structure only, never rows.
        Assert.DoesNotContain("COPY ", ddl);
        Assert.DoesNotContain("INSERT INTO", ddl);
    }

    [Fact]
    public async Task ExportDdl_Returns401_ForAnonymous()
    {
        using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
        var resp = await client.GetAsync(
            $"/api/schema/{Guid.NewGuid()}/ddl",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ExportDdl_Returns403_ForBob()
    {
        var ct = TestContext.Current.CancellationToken;
        var (aliceSession, _, databaseId) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _a = aliceSession;

        using var bobSession = await LoginHelper.SignInAsync("bob", "dev", ct);
        var resp = await bobSession.Client.GetAsync($"/api/schema/{databaseId}/ddl", ct);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task ExportDdl_Returns404_ForUnknownDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, _, _) = await AuthorizedSessionWithBlueServerAsync(ct);
        using var _ = session;

        var resp = await session.Client.GetAsync($"/api/schema/{Guid.NewGuid()}/ddl", ct);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/IntegrationTests --configuration Debug --filter "FullyQualifiedName~ExportDdl"`
Expected: FAIL — the route does not exist yet (404 where 200/403 expected; the 404-for-unknown test may coincidentally pass). If the stack does not run locally, instead confirm via the next build step and rely on CI.

- [ ] **Step 3: Add the endpoint mapping and handler**

In `src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs`:

(a) Add `using System.Text;` to the top with the other `using` directives.

(b) Register the route inside `Map`, right after the existing `GetSchema` mapping:

```csharp
        app.MapGet("/api/schema/{databaseId}/ddl", ExportSchemaDdl)
            .RequireAuthorization()
            .WithName("ExportSchemaDdl");
```

(c) Add the handler method and a filename sanitizer to the `SchemaEndpoints` class (e.g. after `GetSchema`):

```csharp
    private static async Task<Results<FileContentHttpResult, NotFound, BadRequest<string>, ForbidHttpResult>> ExportSchemaDdl(
        DatabaseId databaseId,
        AppDbContext db,
        ICurrentUserAccessor currentUser,
        IServerConnectionFactory connectionFactory,
        ITargetEngine targetEngine,
        CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct);

        var database = await db.Databases.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == databaseId, ct);
        if (database is null)
        {
            return TypedResults.NotFound();
        }

        var hasRole = await db.UserDatabaseRoles.AnyAsync(
            r => r.UserId == user!.Id && r.Permission == Permissions.QueryExecute && r.DatabaseId == databaseId, ct);
        if (!hasRole)
        {
            return TypedResults.Forbid();
        }

        try
        {
            var connectionString = await connectionFactory.GetConnectionStringAsync(databaseId, CredentialKind.Read, ct);
            var ddl = await targetEngine.ExportSchemaDdlAsync(connectionString, ct);

            var bytes = Encoding.UTF8.GetBytes(ddl);
            var fileName = $"{SanitizeFileName(database.DisplayName)}-schema-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sql";
            return TypedResults.File(bytes, "application/sql", fileName);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name
            .Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c)
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "schema" : cleaned;
    }
```

- [ ] **Step 4: Build and regenerate the OpenAPI document**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj --configuration Debug`
Expected: Build succeeded; `src/SluiceBase.Api/openapi.json` now contains a `/api/schema/{databaseId}/ddl` path. Confirm with:

Run: `git diff --stat -- src/SluiceBase.Api/openapi.json`
Expected: shows `openapi.json` modified.

- [ ] **Step 5: Run the endpoint tests (CI executes; locally if your stack runs)**

Run: `dotnet test tests/IntegrationTests --configuration Debug --filter "FullyQualifiedName~ExportDdl"`
Expected: PASS. If the stack does not start locally, rely on CI.

- [ ] **Step 6: Commit**

```bash
git add src/SluiceBase.Api/Endpoints/SchemaEndpoint.cs src/SluiceBase.Api/openapi.json tests/IntegrationTests/SchemaEndpointTests.cs
git commit -m "feat: add schema DDL export endpoint"
```

---

## Task 5: Frontend — download helper, export hook, and Diagram button

**Files:**
- Create: `src/frontend/src/utils/download.ts`
- Test: `src/frontend/src/utils/__tests__/download.test.ts`
- Modify: `src/frontend/src/api/hooks.ts`
- Test: `src/frontend/src/api/__tests__/schema-export-hooks.test.ts`
- Modify: `src/frontend/src/api/schema.ts` (regenerated)
- Modify: `src/frontend/src/routes/_authed/query/diagram.tsx`

- [ ] **Step 1: Regenerate the frontend API types**

Run: `cd src/frontend && npm run gen:api`
Expected: `src/frontend/src/api/schema.ts` updates to include the `/api/schema/{databaseId}/ddl` path. (This must run after Task 4's `openapi.json` regeneration.)

- [ ] **Step 2: Write the failing download-helper test**

Create `src/frontend/src/utils/__tests__/download.test.ts`:

```ts
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { downloadTextFile } from "@/utils/download.ts";

describe("downloadTextFile", () => {
  const mockAnchor = {
    href: "",
    download: "",
    click: vi.fn(),
  };

  beforeEach(() => {
    const originalCreateElement = document.createElement.bind(document);
    vi.stubGlobal("URL", {
      createObjectURL: vi.fn(() => "blob:fake-url"),
      revokeObjectURL: vi.fn(),
    });
    vi.spyOn(document, "createElement").mockImplementation((tag: string) => {
      if (tag === "a") return mockAnchor as unknown as HTMLElement;
      return originalCreateElement(tag);
    });
    mockAnchor.href = "";
    mockAnchor.download = "";
    mockAnchor.click.mockClear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("sets href and download and clicks the anchor", () => {
    downloadTextFile("CREATE TABLE t();", "schema.sql", "application/sql");
    expect(mockAnchor.href).toBe("blob:fake-url");
    expect(mockAnchor.download).toBe("schema.sql");
    expect(mockAnchor.click).toHaveBeenCalledOnce();
  });

  it("revokes the object URL after clicking", () => {
    downloadTextFile("x", "schema.sql", "application/sql");
    expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:fake-url");
  });
});
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/frontend && npx vitest run src/utils/__tests__/download.test.ts`
Expected: FAIL — cannot resolve `@/utils/download.ts`.

- [ ] **Step 4: Implement the download helper**

Create `src/frontend/src/utils/download.ts`:

```ts
export function downloadTextFile(content: string, filename: string, mimeType: string): void {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
```

- [ ] **Step 5: Run the helper test to verify it passes**

Run: `cd src/frontend && npx vitest run src/utils/__tests__/download.test.ts`
Expected: PASS.

- [ ] **Step 6: Write the failing export-hook test**

Create `src/frontend/src/api/__tests__/schema-export-hooks.test.ts`:

```ts
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { beforeEach, describe, expect, it, vi } from "vitest";
import React from "react";
import { useExportSchemaDdl } from "@/api/hooks";

vi.mock("@/api/client", () => ({
  apiRequest: vi.fn(),
  ApiError: class ApiError extends Error {
    constructor(
      public status: number,
      public body: unknown,
    ) {
      super(`API ${status}`);
    }
  },
}));

vi.mock("@/utils/download", () => ({
  downloadTextFile: vi.fn(),
}));

vi.mock("@mantine/notifications", () => ({
  notifications: { show: vi.fn() },
}));

const { apiRequest } = await import("@/api/client");
const { downloadTextFile } = await import("@/utils/download");

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return React.createElement(QueryClientProvider, { client: qc }, children);
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("useExportSchemaDdl", () => {
  it("fetches the DDL and triggers a download", async () => {
    vi.mocked(apiRequest).mockResolvedValue("CREATE TABLE public.users ();");
    const { result } = renderHook(() => useExportSchemaDdl(), { wrapper });

    result.current.mutate({ databaseId: "db-1", filename: "blue-schema.sql" });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(apiRequest).toHaveBeenCalledWith("/api/schema/db-1/ddl");
    expect(downloadTextFile).toHaveBeenCalledWith(
      "CREATE TABLE public.users ();",
      "blue-schema.sql",
      "application/sql",
    );
  });
});
```

- [ ] **Step 7: Run the hook test to verify it fails**

Run: `cd src/frontend && npx vitest run src/api/__tests__/schema-export-hooks.test.ts`
Expected: FAIL — `useExportSchemaDdl` is not exported from `@/api/hooks`.

- [ ] **Step 8: Implement the export hook**

In `src/frontend/src/api/hooks.ts`:

(a) Add this import near the top, with the other `@/...` imports:

```ts
import { downloadTextFile } from "@/utils/download";
```

(b) Add the hook in the schema section (after `useSchemaCompletions`):

```ts
export function useExportSchemaDdl() {
  return useMutation({
    mutationFn: async ({ databaseId, filename }: { databaseId: string; filename: string }) => {
      const ddl = await apiRequest<void, string>(`/api/schema/${databaseId}/ddl`);
      downloadTextFile(ddl, filename, "application/sql");
    },
    onError: (error) => {
      notifications.show({
        title: "Export failed",
        message: error instanceof ApiError ? formatApiError(error) : error.message,
        color: "red",
      });
    },
  });
}
```

- [ ] **Step 9: Run the hook test to verify it passes**

Run: `cd src/frontend && npx vitest run src/api/__tests__/schema-export-hooks.test.ts`
Expected: PASS.

- [ ] **Step 10: Add the "Export DDL" button to the Diagram page**

In `src/frontend/src/routes/_authed/query/diagram.tsx`:

(a) Update the Mantine import to add `Button` and `Group`:

```ts
import { Alert, Box, Button, Center, Group, Loader, Stack, Text } from "@mantine/core";
```

(b) Add the icon import:

```ts
import { IconDownload } from "@tabler/icons-react";
```

(c) Update the hooks import to include the new hook and the catalog hook:

```ts
import { meQueryOptions, useCatalogServer, useExportSchemaDdl, useSchema } from "@/api/hooks";
```

(d) Inside `DiagramPage`, after the existing `const schema = useSchema(selectedDatabaseId);` line, add:

```ts
  const catalog = useCatalogServer();
  const exportDdl = useExportSchemaDdl();

  function handleExport() {
    if (!selectedDatabaseId) return;
    const match = (catalog.data?.servers ?? [])
      .flatMap((s) => s.databases.map((d) => ({ id: d.id, label: `${s.name}-${d.displayName}` })))
      .find((d) => d.id === selectedDatabaseId);
    const base = (match?.label ?? "schema").replace(/[^a-zA-Z0-9._-]/g, "-");
    const timestamp = new Date().toISOString().replace(/[:.]/g, "-");
    exportDdl.mutate({ databaseId: selectedDatabaseId, filename: `${base}-schema-${timestamp}.sql` });
  }
```

(e) Replace the header `Box` (the one containing only `DatabaseSelect`) with a version that adds the button:

```tsx
      <Box p="xs" style={{ borderBottom: "1px solid var(--mantine-color-default-border)" }}>
        <Group justify="space-between" wrap="nowrap">
          <DatabaseSelect value={selectedDatabaseId} onChange={setSelectedDatabaseId} />
          <Button
            leftSection={<IconDownload size={14} />}
            size="sm"
            variant="default"
            disabled={!selectedDatabaseId}
            loading={exportDdl.isPending}
            onClick={handleExport}
          >
            Export DDL
          </Button>
        </Group>
      </Box>
```

- [ ] **Step 11: Lint, type-check, build, and run the full frontend test suite**

Run: `cd src/frontend && npm run lint && npm run build && npx vitest run`
Expected: lint passes (note: `Array<T>` style is enforced — the code above uses none, good), `tsc -b` + `vite build` succeed, all tests pass.

- [ ] **Step 12: Commit**

```bash
git add src/frontend/src/utils/download.ts src/frontend/src/utils/__tests__/download.test.ts src/frontend/src/api/hooks.ts src/frontend/src/api/__tests__/schema-export-hooks.test.ts src/frontend/src/api/schema.ts src/frontend/src/routes/_authed/query/diagram.tsx
git commit -m "feat: add Export DDL button to schema diagram page"
```

---

## Task 6: Final verification

- [ ] **Step 1: Full backend build**

Run: `dotnet build SluiceBase.slnx --configuration Release`
Expected: Build succeeded, 0 warnings/errors.

- [ ] **Step 2: Confirm OpenAPI is current (mirrors the CI gate)**

Run: `dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj --configuration Debug && git diff --exit-code -- src/SluiceBase.Api/openapi.json`
Expected: exit code 0 (no diff) — `openapi.json` was already committed in Task 4.

- [ ] **Step 3: Confirm the frontend generated types are current**

Run: `cd src/frontend && npm run gen:api && git diff --exit-code -- src/api/schema.ts`
Expected: exit code 0 (no diff) — already committed in Task 5.

- [ ] **Step 4: Push and open the PR**

```bash
git push -u origin feat/schema-ddl-export
gh pr create --title "Schema DDL export" --body "$(cat <<'EOF'
## Summary
- Add `pg_dump`-based schema DDL export (`ITargetEngine.ExportSchemaDdlAsync`, Postgres impl using `pg_dump --schema-only --no-owner --no-privileges`)
- Add `GET /api/schema/{databaseId}/ddl` endpoint, reusing the schema-view `query:execute` authorization and read credential
- Add an "Export DDL" button to the `/query/diagram` page
- Hard-enforce `--schema-only` server-side so no table data can ever be exported (sensitive-column / row-based protections out of scope by construction)
- Pin target Postgres images to major 17; install `pg_dump` 17 in the Docker runtime image and CI
EOF
)"
```

---

## Self-Review

**Spec coverage:**
- DDL via real `pg_dump` (spec §1, §2) → Task 3.
- Fixed `--schema-only --no-owner --no-privileges` (spec §2) → Task 3, Step 4.
- Data-protection invariant, structurally enforced + tested (spec §1, §8) → Task 3 (hard-coded flag) + Tasks 3/4 tests asserting no `COPY`/`INSERT`.
- Endpoint with `query:execute` auth, read credential, `404`/`403`/`400`, file response (spec §3, §6) → Task 4.
- Diagram-page "Export DDL" button, disabled with no DB, loading state, error surfaced, filename from display name (spec §4, success criteria) → Task 5.
- `postgres-client` in Dockerfile + version matching (spec §5) → Tasks 1 & 2.
- Integration tests (export content, `403`, `404`) + frontend tests (spec §8) → Tasks 3, 4, 5. Note: frontend coverage is at hook + util level (matching the repo's existing test style) rather than a full route render; the disabled/loading wiring is covered by TypeScript + lint/build. This is a deliberate, minor deviation from spec §8's phrasing.
- `401` for anonymous is also covered (Task 4) for parity with existing schema tests.

**Placeholder scan:** No TBD/TODO/"handle errors"/"similar to" placeholders; every code and command step contains concrete content.

**Type consistency:** `ExportSchemaDdlAsync(string, CancellationToken) → Task<string>` is identical across the interface (Task 3 Step 3), the implementation (Step 4), and both callers (engine test Step 1, endpoint Step 3). The hook mutation variables `{ databaseId, filename }` match between the hook (Task 5 Step 8), its test (Step 6), and the page caller (Step 10). `downloadTextFile(content, filename, mimeType)` is identical across helper (Step 4), its test (Step 2), hook (Step 8), and hook test (Step 6).
