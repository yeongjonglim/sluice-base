# Update Approval Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the full update request lifecycle — submit, approve/reject, cancel, execute — with a Stateless state machine, 7 API endpoints, EF migration, and three frontend routes.

**Architecture:** Domain entity `UpdateRequest` lives in `SluiceBase.Core`; a Stateless machine in `SluiceBase.Api` guards state transitions; seven minimal-API endpoints follow the existing `ServerEndpoints` pattern. The frontend adds three TanStack Router routes under `/_authed/update/` and seven new hooks.

**Tech Stack:** .NET 10, Stateless (NuGet), EF Core + Npgsql, React + TypeScript, Mantine, TanStack Router/Query, Vitest

---

## File Map

**Create (backend):**
- `src/SluiceBase.Core/Updates/UpdateRequestId.cs`
- `src/SluiceBase.Core/Updates/UpdateRequestStatus.cs`
- `src/SluiceBase.Core/Updates/UpdateRequest.cs`
- `src/SluiceBase.Api/Updates/UpdateRequestTrigger.cs`
- `src/SluiceBase.Api/Updates/UpdateRequestMachine.cs`
- `src/SluiceBase.Api/Auth/AnyPermissionRequirement.cs`
- `src/SluiceBase.Api/Auth/AnyPermissionAuthorizationHandler.cs`
- `src/SluiceBase.Api/Data/Configurations/UpdateRequestConfiguration.cs`
- `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs`
- `tests/IntegrationTests/UpdateEndpointTests.cs`

**Modify (backend):**
- `src/SluiceBase.Core/Permissions/Permissions.cs` — add `UpdateAny` constant
- `src/SluiceBase.Api/SluiceBase.Api.csproj` — add Stateless NuGet
- `src/SluiceBase.Api/Data/AppDbContext.cs` — add `DbSet<UpdateRequest>`
- `src/SluiceBase.Api/Auth/AuthSetup.cs` — register new policy + handler
- `src/SluiceBase.Api/Endpoints/EndpointMapper.cs` — register `UpdateEndpoints`
- `src/SluiceBase.Core/Targets/ITargetEngine.cs` — add `ExecuteUpdateAsync`
- `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs` — implement `ExecuteUpdateAsync`

**Create (frontend):**
- `src/frontend/src/routes/_authed/update/index.tsx`
- `src/frontend/src/routes/_authed/update/new.tsx`
- `src/frontend/src/routes/_authed/update/$id.tsx`
- `src/frontend/src/api/__tests__/update-hooks.test.ts`

**Modify (frontend):**
- `src/frontend/src/api/hooks.ts` — add 7 hooks
- `src/frontend/src/routes/_authed.tsx` — add Updates nav link

---

## Task 1: Stateless NuGet + core domain types

**Files:**
- Modify: `src/SluiceBase.Api/SluiceBase.Api.csproj`
- Create: `src/SluiceBase.Core/Updates/UpdateRequestId.cs`
- Create: `src/SluiceBase.Core/Updates/UpdateRequestStatus.cs`
- Create: `src/SluiceBase.Core/Updates/UpdateRequest.cs`
- Modify: `src/SluiceBase.Core/Permissions/Permissions.cs`

- [ ] **Step 1: Add Stateless to SluiceBase.Api.csproj**

  Run from repo root:
  ```bash
  dotnet add src/SluiceBase.Api/SluiceBase.Api.csproj package Stateless
  ```
  Expected: NuGet resolves and adds a `<PackageReference Include="Stateless" ... />` line.

- [ ] **Step 2: Create UpdateRequestId**

  `src/SluiceBase.Core/Updates/UpdateRequestId.cs`:
  ```csharp
  using Vogen;

  namespace SluiceBase.Core.Updates;

  [ValueObject<Guid>(conversions: Conversions.SystemTextJson, customizations: Customizations.AddFactoryMethodForGuids)]
  public readonly partial struct UpdateRequestId;
  ```

- [ ] **Step 3: Create UpdateRequestStatus**

  `src/SluiceBase.Core/Updates/UpdateRequestStatus.cs`:
  ```csharp
  namespace SluiceBase.Core.Updates;

  public static class UpdateRequestStatus
  {
      public const string Pending = "pending";
      public const string Approved = "approved";
      public const string Rejected = "rejected";
      public const string Cancelled = "cancelled";
      public const string Executed = "executed";
  }
  ```

- [ ] **Step 4: Create UpdateRequest entity**

  `src/SluiceBase.Core/Updates/UpdateRequest.cs`:
  ```csharp
  using SluiceBase.Core.Servers;
  using SluiceBase.Core.Users;

  namespace SluiceBase.Core.Updates;

  public sealed class UpdateRequest
  {
  #pragma warning disable CS8618
      private UpdateRequest() { }
  #pragma warning restore CS8618

      public UpdateRequestId Id { get; private set; }
      public ServerId? ServerId { get; private set; }
      public UserId? SubmitterId { get; private set; }
      public string SqlText { get; private set; }
      public string Reason { get; private set; }
      public string Status { get; private set; }
      public UserId? ReviewerId { get; private set; }
      public string? ReviewNote { get; private set; }
      public UserId? ExecutorId { get; private set; }
      public DateTimeOffset SubmittedAt { get; private set; }
      public DateTimeOffset? ReviewedAt { get; private set; }
      public DateTimeOffset? ExecutedAt { get; private set; }
      public bool? ExecSuccess { get; private set; }
      public int? ExecDurationMs { get; private set; }
      public int? ExecAffectedRows { get; private set; }
      public string? ExecError { get; private set; }

      public Server? Server { get; private set; }
      public User? Submitter { get; private set; }
      public User? Reviewer { get; private set; }
      public User? Executor { get; private set; }

      public static UpdateRequest Create(
          ServerId serverId,
          UserId submitterId,
          string sqlText,
          string reason,
          DateTimeOffset at) => new()
      {
          Id = UpdateRequestId.FromNewVersion7Guid(),
          ServerId = serverId,
          SubmitterId = submitterId,
          SqlText = sqlText,
          Reason = reason,
          Status = UpdateRequestStatus.Pending,
          SubmittedAt = at,
      };

      public void Approve(UserId reviewerId, string note, DateTimeOffset at)
      {
          Status = UpdateRequestStatus.Approved;
          ReviewerId = reviewerId;
          ReviewNote = note;
          ReviewedAt = at;
      }

      public void Reject(UserId reviewerId, string note, DateTimeOffset at)
      {
          Status = UpdateRequestStatus.Rejected;
          ReviewerId = reviewerId;
          ReviewNote = note;
          ReviewedAt = at;
      }

      public void Cancel()
      {
          Status = UpdateRequestStatus.Cancelled;
      }

      public void SetExecutionResult(
          UserId executorId,
          DateTimeOffset at,
          bool success,
          int durationMs,
          int? affectedRows,
          string? error)
      {
          Status = UpdateRequestStatus.Executed;
          ExecutorId = executorId;
          ExecutedAt = at;
          ExecSuccess = success;
          ExecDurationMs = durationMs;
          ExecAffectedRows = affectedRows;
          ExecError = error;
      }
  }
  ```

- [ ] **Step 5: Add UpdateAny to Permissions.cs**

  In `src/SluiceBase.Core/Permissions/Permissions.cs`, add after `UpdateExecute`:
  ```csharp
  // Virtual policy — never assigned to users; combines update:submit|approve|execute for read access
  public const string UpdateAny = "update:any";
  ```
  Do NOT add `UpdateAny` to the `All` set.

- [ ] **Step 6: Build to verify no errors**

  ```bash
  dotnet build src/SluiceBase.Core/SluiceBase.Core.csproj
  ```
  Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 7: Commit**

  ```bash
  git add src/SluiceBase.Core/Updates/ src/SluiceBase.Core/Permissions/Permissions.cs src/SluiceBase.Api/SluiceBase.Api.csproj
  git commit -m "feat: add UpdateRequest domain entity and Stateless NuGet"
  ```

---

## Task 2: State machine factory + any-permission auth policy

**Files:**
- Create: `src/SluiceBase.Api/Updates/UpdateRequestTrigger.cs`
- Create: `src/SluiceBase.Api/Updates/UpdateRequestMachine.cs`
- Create: `src/SluiceBase.Api/Auth/AnyPermissionRequirement.cs`
- Create: `src/SluiceBase.Api/Auth/AnyPermissionAuthorizationHandler.cs`
- Modify: `src/SluiceBase.Api/Auth/AuthSetup.cs`

- [ ] **Step 1: Create UpdateRequestTrigger**

  `src/SluiceBase.Api/Updates/UpdateRequestTrigger.cs`:
  ```csharp
  namespace SluiceBase.Api.Updates;

  internal static class UpdateRequestTrigger
  {
      public const string Approve = "Approve";
      public const string Reject = "Reject";
      public const string Cancel = "Cancel";
      public const string Execute = "Execute";
  }
  ```

- [ ] **Step 2: Create UpdateRequestMachine**

  `src/SluiceBase.Api/Updates/UpdateRequestMachine.cs`:
  ```csharp
  using Stateless;
  using SluiceBase.Core.Updates;

  namespace SluiceBase.Api.Updates;

  internal static class UpdateRequestMachine
  {
      public static StateMachine<string, string> Build(string currentStatus)
      {
          var machine = new StateMachine<string, string>(currentStatus);

          machine.Configure(UpdateRequestStatus.Pending)
              .Permit(UpdateRequestTrigger.Approve, UpdateRequestStatus.Approved)
              .Permit(UpdateRequestTrigger.Reject, UpdateRequestStatus.Rejected)
              .Permit(UpdateRequestTrigger.Cancel, UpdateRequestStatus.Cancelled);

          machine.Configure(UpdateRequestStatus.Approved)
              .Permit(UpdateRequestTrigger.Cancel, UpdateRequestStatus.Cancelled)
              .Permit(UpdateRequestTrigger.Execute, UpdateRequestStatus.Executed);

          machine.Configure(UpdateRequestStatus.Rejected);
          machine.Configure(UpdateRequestStatus.Cancelled);
          machine.Configure(UpdateRequestStatus.Executed);

          return machine;
      }
  }
  ```

- [ ] **Step 3: Create AnyPermissionRequirement**

  `src/SluiceBase.Api/Auth/AnyPermissionRequirement.cs`:
  ```csharp
  using Microsoft.AspNetCore.Authorization;

  namespace SluiceBase.Api.Auth;

  internal sealed class AnyPermissionRequirement(IReadOnlyList<string> permissions) : IAuthorizationRequirement
  {
      public IReadOnlyList<string> Permissions { get; } = permissions;
  }
  ```

- [ ] **Step 4: Create AnyPermissionAuthorizationHandler**

  `src/SluiceBase.Api/Auth/AnyPermissionAuthorizationHandler.cs`:
  ```csharp
  using Microsoft.AspNetCore.Authorization;

  namespace SluiceBase.Api.Auth;

  internal sealed class AnyPermissionAuthorizationHandler(
      ICurrentUserAccessor currentUser) : AuthorizationHandler<AnyPermissionRequirement>
  {
      protected override async Task HandleRequirementAsync(
          AuthorizationHandlerContext ctx, AnyPermissionRequirement req)
      {
          var user = await currentUser.GetAsync(CancellationToken.None);
          if (user is not null && req.Permissions.Any(p => user.HasPermission(p)))
          {
              ctx.Succeed(req);
          }
      }
  }
  ```

- [ ] **Step 5: Register policy and handler in AuthSetup.cs**

  In `src/SluiceBase.Api/Auth/AuthSetup.cs`, inside `AddAuthorization`, after the `foreach` loop that registers individual permission policies, add:
  ```csharp
  options.AddPolicy(Permissions.UpdateAny, policy =>
      policy.Requirements.Add(new AnyPermissionRequirement([
          Permissions.UpdateSubmit,
          Permissions.UpdateApprove,
          Permissions.UpdateExecute,
      ])));
  ```

  Also register the handler after `services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();`:
  ```csharp
  services.AddScoped<IAuthorizationHandler, AnyPermissionAuthorizationHandler>();
  ```

- [ ] **Step 6: Build**

  ```bash
  dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
  ```
  Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 7: Commit**

  ```bash
  git add src/SluiceBase.Api/Updates/ src/SluiceBase.Api/Auth/
  git commit -m "feat: add state machine factory and update:any auth policy"
  ```

---

## Task 3: EF configuration + DbContext + migration

**Files:**
- Create: `src/SluiceBase.Api/Data/Configurations/UpdateRequestConfiguration.cs`
- Modify: `src/SluiceBase.Api/Data/AppDbContext.cs`

- [ ] **Step 1: Create UpdateRequestConfiguration**

  `src/SluiceBase.Api/Data/Configurations/UpdateRequestConfiguration.cs`:
  ```csharp
  using Microsoft.EntityFrameworkCore;
  using Microsoft.EntityFrameworkCore.Metadata.Builders;
  using SluiceBase.Core.Servers;
  using SluiceBase.Core.Updates;
  using SluiceBase.Core.Users;

  namespace SluiceBase.Api.Data.Configurations;

  internal sealed class UpdateRequestConfiguration : IEntityTypeConfiguration<UpdateRequest>
  {
      public void Configure(EntityTypeBuilder<UpdateRequest> builder)
      {
          builder.ToTable("update_request");
          builder.HasKey(r => r.Id);
          builder.Property(r => r.SqlText).IsRequired();
          builder.Property(r => r.Reason).IsRequired();
          builder.Property(r => r.Status).HasMaxLength(16).IsRequired();

          builder.HasOne(r => r.Server).WithMany()
              .HasForeignKey(r => r.ServerId)
              .OnDelete(DeleteBehavior.SetNull);

          builder.HasOne(r => r.Submitter).WithMany()
              .HasForeignKey(r => r.SubmitterId)
              .OnDelete(DeleteBehavior.SetNull);

          builder.HasOne(r => r.Reviewer).WithMany()
              .HasForeignKey(r => r.ReviewerId)
              .OnDelete(DeleteBehavior.SetNull);

          builder.HasOne(r => r.Executor).WithMany()
              .HasForeignKey(r => r.ExecutorId)
              .OnDelete(DeleteBehavior.SetNull);
      }
  }
  ```

- [ ] **Step 2: Add DbSet to AppDbContext**

  In `src/SluiceBase.Api/Data/AppDbContext.cs`, add the using and DbSet:
  ```csharp
  using SluiceBase.Core.Updates;
  ```
  ```csharp
  public DbSet<UpdateRequest> UpdateRequests => Set<UpdateRequest>();
  ```

- [ ] **Step 3: Generate migration**

  Run from repo root:
  ```bash
  dotnet ef migrations add AddUpdateRequest --project src/SluiceBase.Api --startup-project src/SluiceBase.Api
  ```
  Expected: `Done. To undo this action, use 'ef migrations remove'`

  Open the generated file at `src/SluiceBase.Api/Data/Migrations/<timestamp>_AddUpdateRequest.cs` and verify the `Up` method creates a `update_request` table with columns:
  `id`, `server_id`, `submitter_id`, `sql_text`, `reason`, `status`, `reviewer_id`, `review_note`, `executor_id`, `submitted_at`, `reviewed_at`, `executed_at`, `exec_success`, `exec_duration_ms`, `exec_affected_rows`, `exec_error`

  And four foreign keys with `ON DELETE SET NULL` pointing to `server`, `user`, `user`, `user`.

- [ ] **Step 4: Build**

  ```bash
  dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
  ```
  Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 5: Commit**

  ```bash
  git add src/SluiceBase.Api/Data/
  git commit -m "feat: add UpdateRequest EF configuration and migration"
  ```

---

## Task 4: ExecuteUpdateAsync on ITargetEngine and PostgresTargetEngine

**Files:**
- Modify: `src/SluiceBase.Core/Targets/ITargetEngine.cs`
- Modify: `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`
- Test: `tests/IntegrationTests/TargetEngineTests.cs`

- [ ] **Step 1: Write failing test**

  In `tests/IntegrationTests/TargetEngineTests.cs`, add at the end of the class:
  ```csharp
  [Fact]
  public async Task TargetEngine_Postgres_ExecuteUpdate_AffectsRows()
  {
      var ct = TestContext.Current.CancellationToken;
      var connectionString = await factory.InitialisedApp
          .GetConnectionStringAsync("blue-appdb", ct);
      Assert.NotNull(connectionString);

      // Use write credentials from the blue DB seed (writer_blue/writer_blue)
      var builder = new NpgsqlConnectionStringBuilder(connectionString!)
      {
          Username = "writer_blue",
          Password = "writer_blue",
      };

      var result = await _targetEngine.ExecuteUpdateAsync(
          builder.ConnectionString,
          "UPDATE public.users SET email = email WHERE 1=0",
          ct);

      Assert.Equal(0, result);
  }

  [Fact]
  public async Task TargetEngine_Postgres_ExecuteUpdate_ThrowsOnInvalidSql()
  {
      var ct = TestContext.Current.CancellationToken;
      var connectionString = await factory.InitialisedApp
          .GetConnectionStringAsync("blue-appdb", ct);
      Assert.NotNull(connectionString);

      var builder = new NpgsqlConnectionStringBuilder(connectionString!)
      {
          Username = "writer_blue",
          Password = "writer_blue",
      };

      await Assert.ThrowsAsync<PostgresException>(async () =>
          await _targetEngine.ExecuteUpdateAsync(
              builder.ConnectionString,
              "UPDATE public.nonexistent SET foo = bar",
              ct));
  }
  ```

- [ ] **Step 2: Run tests to verify they fail**

  ```bash
  dotnet test tests/IntegrationTests/ --filter "FullyQualifiedName~TargetEngineTests.TargetEngine_Postgres_ExecuteUpdate"
  ```
  Expected: FAIL — `ITargetEngine` does not have `ExecuteUpdateAsync`.

- [ ] **Step 3: Add ExecuteUpdateAsync to ITargetEngine**

  In `src/SluiceBase.Core/Targets/ITargetEngine.cs`, add after `ExecuteQueryAsync`:
  ```csharp
  Task<int> ExecuteUpdateAsync(
      string connectionString,
      string sql,
      CancellationToken ct);
  ```

- [ ] **Step 4: Implement ExecuteUpdateAsync in PostgresTargetEngine**

  In `src/SluiceBase.Api/Targets/PostgresTargetEngine.cs`, add after `ExecuteQueryAsync`:
  ```csharp
  public async Task<int> ExecuteUpdateAsync(string connectionString, string sql, CancellationToken ct)
  {
      await using var conn = new NpgsqlConnection(connectionString);
      await conn.OpenAsync(ct);
      await using var cmd = new NpgsqlCommand(sql, conn);
      return await cmd.ExecuteNonQueryAsync(ct);
  }
  ```

- [ ] **Step 5: Run tests to verify they pass**

  ```bash
  dotnet test tests/IntegrationTests/ --filter "FullyQualifiedName~TargetEngineTests.TargetEngine_Postgres_ExecuteUpdate"
  ```
  Expected: PASS (2 tests).

- [ ] **Step 6: Build to ensure no other tests broke**

  ```bash
  dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
  ```
  Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 7: Commit**

  ```bash
  git add src/SluiceBase.Core/Targets/ITargetEngine.cs src/SluiceBase.Api/Targets/PostgresTargetEngine.cs tests/IntegrationTests/TargetEngineTests.cs
  git commit -m "feat: add ExecuteUpdateAsync to ITargetEngine and PostgresTargetEngine"
  ```

---

## Task 5: UpdateEndpoints — submit, list, get — and EndpointMapper

**Files:**
- Create: `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs`
- Modify: `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`

- [ ] **Step 1: Create UpdateEndpoints.cs with submit, list, get**

  `src/SluiceBase.Api/Endpoints/UpdateEndpoints.cs`:
  ```csharp
  using Microsoft.AspNetCore.Http.HttpResults;
  using Microsoft.EntityFrameworkCore;
  using SluiceBase.Api.Auth;
  using SluiceBase.Api.Data;
  using SluiceBase.Api.Updates;
  using SluiceBase.Core.Permissions;
  using SluiceBase.Core.Servers;
  using SluiceBase.Core.Updates;
  using SluiceBase.Core.Users;

  namespace SluiceBase.Api.Endpoints;

  internal static class UpdateEndpoints
  {
      public static void Map(IEndpointRouteBuilder app)
      {
          var group = app.MapGroup("/api/update").RequireAuthorization();

          group.MapPost("/", Submit)
              .RequireAuthorization(Permissions.UpdateSubmit)
              .WithName("SubmitUpdate");

          group.MapGet("/", List)
              .RequireAuthorization(Permissions.UpdateAny)
              .WithName("ListUpdates");

          group.MapGet("/{id}", Get)
              .RequireAuthorization(Permissions.UpdateAny)
              .WithName("GetUpdate");

          group.MapPost("/{id}/approve", Approve)
              .RequireAuthorization(Permissions.UpdateApprove)
              .WithName("ApproveUpdate");

          group.MapPost("/{id}/reject", Reject)
              .RequireAuthorization(Permissions.UpdateApprove)
              .WithName("RejectUpdate");

          group.MapPost("/{id}/cancel", Cancel)
              .RequireAuthorization(Permissions.UpdateSubmit)
              .WithName("CancelUpdate");

          group.MapPost("/{id}/execute", Execute)
              .RequireAuthorization(Permissions.UpdateExecute)
              .WithName("ExecuteUpdate");
      }

      // ── submit ───────────────────────────────────────────────────────────────

      private static async Task<Results<Created<UpdateRequestDetailResponse>, BadRequest<string>, NotFound>> Submit(
          SubmitUpdateRequest req,
          AppDbContext db,
          ICurrentUserAccessor currentUser,
          TimeProvider timeProvider,
          CancellationToken ct)
      {
          var user = await currentUser.GetAsync(ct);

          var server = await db.Servers.AsNoTracking()
              .SingleOrDefaultAsync(s => s.Id == req.ServerId, ct);
          if (server is null)
          {
              return TypedResults.NotFound();
          }

          if (!server.HasWriteCredential)
          {
              return TypedResults.BadRequest("Server has no write credentials configured.");
          }

          var request = UpdateRequest.Create(
              server.Id,
              user!.Id,
              req.SqlText,
              req.Reason,
              timeProvider.GetUtcNow());

          db.UpdateRequests.Add(request);
          await db.SaveChangesAsync(ct);

          var created = await LoadDetail(db, request.Id, ct);
          return TypedResults.Created($"/api/update/{request.Id}", ToDetail(created!));
      }

      // ── list ─────────────────────────────────────────────────────────────────

      private static async Task<Ok<ListUpdateRequestsResponse>> List(
          AppDbContext db,
          CancellationToken ct)
      {
          var requests = await db.UpdateRequests
              .Include(r => r.Server)
              .Include(r => r.Submitter)
              .AsNoTracking()
              .OrderByDescending(r => r.SubmittedAt)
              .ToListAsync(ct);

          var items = requests
              .Select(r => new UpdateSummaryItem(
                  r.Id,
                  r.Server?.Name,
                  r.Submitter?.Name ?? r.Submitter?.Email,
                  r.Reason,
                  r.Status,
                  r.SubmittedAt,
                  r.ExecSuccess))
              .ToList();

          return TypedResults.Ok(new ListUpdateRequestsResponse(items));
      }

      // ── get ──────────────────────────────────────────────────────────────────

      private static async Task<Results<Ok<UpdateRequestDetailResponse>, NotFound>> Get(
          UpdateRequestId id,
          AppDbContext db,
          CancellationToken ct)
      {
          var request = await LoadDetail(db, id, ct);
          if (request is null)
          {
              return TypedResults.NotFound();
          }

          return TypedResults.Ok(ToDetail(request));
      }

      // ── approve ──────────────────────────────────────────────────────────────

      private static async Task<Results<Ok<UpdateRequestDetailResponse>, NotFound, Conflict<string>>> Approve(
          UpdateRequestId id,
          ReviewUpdateRequest req,
          AppDbContext db,
          ICurrentUserAccessor currentUser,
          TimeProvider timeProvider,
          CancellationToken ct)
      {
          var request = await LoadForMutation(db, id, ct);
          if (request is null)
          {
              return TypedResults.NotFound();
          }

          var machine = UpdateRequestMachine.Build(request.Status);
          if (!machine.CanFire(UpdateRequestTrigger.Approve))
          {
              return TypedResults.Conflict($"Cannot approve a request in '{request.Status}' state.");
          }

          var user = await currentUser.GetAsync(ct);
          request.Approve(user!.Id, req.Note, timeProvider.GetUtcNow());
          await db.SaveChangesAsync(ct);

          return TypedResults.Ok(ToDetail(await LoadDetail(db, id, ct)!));
      }

      // ── reject ───────────────────────────────────────────────────────────────

      private static async Task<Results<Ok<UpdateRequestDetailResponse>, NotFound, Conflict<string>>> Reject(
          UpdateRequestId id,
          ReviewUpdateRequest req,
          AppDbContext db,
          ICurrentUserAccessor currentUser,
          TimeProvider timeProvider,
          CancellationToken ct)
      {
          var request = await LoadForMutation(db, id, ct);
          if (request is null)
          {
              return TypedResults.NotFound();
          }

          var machine = UpdateRequestMachine.Build(request.Status);
          if (!machine.CanFire(UpdateRequestTrigger.Reject))
          {
              return TypedResults.Conflict($"Cannot reject a request in '{request.Status}' state.");
          }

          var user = await currentUser.GetAsync(ct);
          request.Reject(user!.Id, req.Note, timeProvider.GetUtcNow());
          await db.SaveChangesAsync(ct);

          return TypedResults.Ok(ToDetail(await LoadDetail(db, id, ct)!));
      }

      // ── cancel ───────────────────────────────────────────────────────────────

      private static async Task<Results<Ok<UpdateRequestDetailResponse>, NotFound, Conflict<string>>> Cancel(
          UpdateRequestId id,
          AppDbContext db,
          CancellationToken ct)
      {
          var request = await LoadForMutation(db, id, ct);
          if (request is null)
          {
              return TypedResults.NotFound();
          }

          var machine = UpdateRequestMachine.Build(request.Status);
          if (!machine.CanFire(UpdateRequestTrigger.Cancel))
          {
              return TypedResults.Conflict($"Cannot cancel a request in '{request.Status}' state.");
          }

          request.Cancel();
          await db.SaveChangesAsync(ct);

          return TypedResults.Ok(ToDetail(await LoadDetail(db, id, ct)!));
      }

      // ── execute ──────────────────────────────────────────────────────────────

      private static async Task<Results<Ok<UpdateRequestDetailResponse>, NotFound, Conflict<string>, BadRequest<string>>> Execute(
          UpdateRequestId id,
          AppDbContext db,
          ICurrentUserAccessor currentUser,
          IServerConnectionFactory connectionFactory,
          ITargetEngine targetEngine,
          TimeProvider timeProvider,
          IConfiguration configuration,
          CancellationToken ct)
      {
          var request = await LoadForMutation(db, id, ct);
          if (request is null)
          {
              return TypedResults.NotFound();
          }

          var machine = UpdateRequestMachine.Build(request.Status);
          if (!machine.CanFire(UpdateRequestTrigger.Execute))
          {
              return TypedResults.Conflict($"Cannot execute a request in '{request.Status}' state.");
          }

          if (request.ServerId is null)
          {
              return TypedResults.Conflict("Server was deleted. Cannot execute.");
          }

          var server = await db.Servers.AsNoTracking()
              .SingleOrDefaultAsync(s => s.Id == request.ServerId, ct);
          if (server is null || !server.HasWriteCredential)
          {
              return TypedResults.Conflict("Server not found or has no write credentials configured.");
          }

          var timeoutSeconds = configuration.GetValue("Query:TimeoutSeconds", 30);
          using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
          using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

          var user = await currentUser.GetAsync(ct);
          var startedAt = timeProvider.GetUtcNow();

          bool success;
          int? affectedRows = null;
          string? execError = null;

          try
          {
              var connectionString = await connectionFactory
                  .GetConnectionStringAsync(server.Id, CredentialKind.Write, ct);
              var raw = await targetEngine.ExecuteUpdateAsync(
                  connectionString, request.SqlText, linkedCts.Token);
              affectedRows = raw >= 0 ? raw : null;
              success = true;
          }
          catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
          {
              success = false;
              execError = $"Execution timed out after {timeoutSeconds}s.";
          }
          catch (Exception ex) when (ex is not OperationCanceledException)
          {
              success = false;
              execError = ex.Message;
          }

          var durationMs = (int)(timeProvider.GetUtcNow() - startedAt).TotalMilliseconds;
          request.SetExecutionResult(user!.Id, timeProvider.GetUtcNow(), success, durationMs, affectedRows, execError);
          await db.SaveChangesAsync(ct);

          return TypedResults.Ok(ToDetail(await LoadDetail(db, id, ct)!));
      }

      // ── helpers ───────────────────────────────────────────────────────────────

      // AsNoTracking so that a second call after SaveChangesAsync returns fresh data with nav props.
      private static Task<UpdateRequest?> LoadDetail(AppDbContext db, UpdateRequestId id, CancellationToken ct) =>
          db.UpdateRequests
              .AsNoTracking()
              .Include(r => r.Server)
              .Include(r => r.Submitter)
              .Include(r => r.Reviewer)
              .Include(r => r.Executor)
              .SingleOrDefaultAsync(r => r.Id == id, ct);

      // Tracked load for state-transition endpoints that need to mutate the entity.
      private static Task<UpdateRequest?> LoadForMutation(AppDbContext db, UpdateRequestId id, CancellationToken ct) =>
          db.UpdateRequests.SingleOrDefaultAsync(r => r.Id == id, ct);

      private static UpdateRequestDetailResponse ToDetail(UpdateRequest r) =>
          new(r.Id,
              r.ServerId,
              r.Server?.Name,
              r.SubmitterId,
              r.Submitter?.Name ?? r.Submitter?.Email,
              r.SqlText,
              r.Reason,
              r.Status,
              r.ReviewerId,
              r.Reviewer?.Name ?? r.Reviewer?.Email,
              r.ReviewNote,
              r.ExecutorId,
              r.Executor?.Name ?? r.Executor?.Email,
              r.SubmittedAt,
              r.ReviewedAt,
              r.ExecutedAt,
              r.ExecSuccess,
              r.ExecDurationMs,
              r.ExecAffectedRows,
              r.ExecError);

      // ── request / response records ────────────────────────────────────────────

      public sealed record SubmitUpdateRequest(ServerId ServerId, string SqlText, string Reason);
      public sealed record ReviewUpdateRequest(string Note);

      public sealed record UpdateSummaryItem(
          UpdateRequestId Id,
          string? ServerName,
          string? SubmitterName,
          string Reason,
          string Status,
          DateTimeOffset SubmittedAt,
          bool? ExecSuccess);

      public sealed record ListUpdateRequestsResponse(IReadOnlyList<UpdateSummaryItem> Requests);

      public sealed record UpdateRequestDetailResponse(
          UpdateRequestId Id,
          ServerId? ServerId,
          string? ServerName,
          UserId? SubmitterId,
          string? SubmitterName,
          string SqlText,
          string Reason,
          string Status,
          UserId? ReviewerId,
          string? ReviewerName,
          string? ReviewNote,
          UserId? ExecutorId,
          string? ExecutorName,
          DateTimeOffset SubmittedAt,
          DateTimeOffset? ReviewedAt,
          DateTimeOffset? ExecutedAt,
          bool? ExecSuccess,
          int? ExecDurationMs,
          int? ExecAffectedRows,
          string? ExecError);
  }
  ```

  Note: the `Approve`, `Reject`, `Cancel`, `Execute` method bodies in `UpdateEndpoints.cs` have been written above all together — they're part of the same file created in this step, not separate steps.

- [ ] **Step 2: Add missing IServerConnectionFactory and ITargetEngine usings**

  The `Execute` endpoint needs `IServerConnectionFactory` and `ITargetEngine`. Add these usings at the top of `UpdateEndpoints.cs` if not already present:
  ```csharp
  using SluiceBase.Api.Servers;
  using SluiceBase.Core.Targets;
  ```

- [ ] **Step 3: Register in EndpointMapper.cs**

  In `src/SluiceBase.Api/Endpoints/EndpointMapper.cs`, add before the `if (app.Environment.IsDevelopment())` block:
  ```csharp
  UpdateEndpoints.Map(app);
  ```

- [ ] **Step 4: Build**

  ```bash
  dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
  ```
  Expected: Build succeeded, 0 Warning(s), 0 Error(s).

- [ ] **Step 5: Commit**

  ```bash
  git add src/SluiceBase.Api/Endpoints/ src/SluiceBase.Api/Updates/
  git commit -m "feat: add UpdateEndpoints with all 7 routes"
  ```

---

## Task 6: Integration tests

**Files:**
- Create: `tests/IntegrationTests/UpdateEndpointTests.cs`

- [ ] **Step 1: Write tests**

  `tests/IntegrationTests/UpdateEndpointTests.cs`:
  ```csharp
  using System.Net;
  using System.Net.Http.Json;
  using Aspire.Hosting.Testing;
  using IntegrationTests.Supports;
  using Npgsql;
  using SluiceBase.Api.Endpoints;
  using SluiceBase.Core.Permissions;

  namespace IntegrationTests;

  public class UpdateEndpointTests(SluiceBaseStackFactory factory)
  {
      private KeycloakLoginHelper LoginHelper => new(factory.InitialisedApp);

      private static HttpRequestMessage MutationRequest(
          HttpMethod method, string url, string xsrf, object? body = null)
      {
          var req = new HttpRequestMessage(method, url);
          req.Headers.Add("X-XSRF-TOKEN", xsrf);
          if (body is not null)
          {
              req.Content = JsonContent.Create(body);
          }

          return req;
      }

      // Sets up Alice with all update permissions and returns the blue server's ID
      private async Task<(AuthenticatedSession session, string serverId)> AliceWithBlueServerAsync(
          string[] permissions,
          CancellationToken ct)
      {
          var session = await LoginHelper.SignInAsync("alice", "dev", ct);
          var xsrf = await session.FetchXsrfTokenAsync(ct);

          var users = await session.Client.GetFromJsonAsync<ListUsersResponse>("/api/admin/user", ct);
          var alice = users!.Users.Single(u => u.Email == "alice@example.com");

          foreach (var perm in permissions)
          {
              using var grant = MutationRequest(HttpMethod.Post,
                  $"/api/admin/user/{alice.Id}/permission",
                  xsrf, new { permission = perm });
              (await session.Client.SendAsync(grant, ct)).EnsureSuccessStatusCode();
          }

          // Register blue server with write credentials
          var blueConnStr = await factory.InitialisedApp.GetConnectionStringAsync("blue-appdb", ct);
          var blueBuilder = new NpgsqlConnectionStringBuilder(blueConnStr!);

          var serverName = $"upd-{Guid.NewGuid():N}"[..24];
          using var createReq = MutationRequest(HttpMethod.Post, "/api/server", xsrf,
              new ServerEndpoints.CreateServerRequest(
                  serverName,
                  "postgres",
                  blueBuilder.Host!,
                  blueBuilder.Port,
                  "appdb",
                  "reader_blue",
                  "reader_blue",
                  "writer_blue",
                  "writer_blue"));

          // Need server:manage permission for server creation; grant it temporarily
          using var grantServer = MutationRequest(HttpMethod.Post,
              $"/api/admin/user/{alice.Id}/permission", xsrf,
              new { permission = Permissions.ServerManage });
          (await session.Client.SendAsync(grantServer, ct)).EnsureSuccessStatusCode();

          var createResp = await session.Client.SendAsync(createReq, ct);
          createResp.EnsureSuccessStatusCode();
          var server = await createResp.Content.ReadFromJsonAsync<ServerEndpoints.ServerResponse>(ct);

          return (session, server!.Id.Value.ToString());
      }

      // ── auth guards ──────────────────────────────────────────────────────────

      [Fact]
      public async Task PostUpdate_Returns401_ForAnonymous()
      {
          using var client = factory.InitialisedApp.CreateHttpClient("api", "https");
          using var req = new HttpRequestMessage(HttpMethod.Post, "/api/update");
          req.Content = JsonContent.Create(new { serverId = Guid.NewGuid(), sqlText = "UPDATE x", reason = "r" });
          var resp = await client.SendAsync(req, TestContext.Current.CancellationToken);
          Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
      }

      [Fact]
      public async Task PostUpdate_Returns403_ForUserWithoutPermission()
      {
          var ct = TestContext.Current.CancellationToken;
          using var session = await LoginHelper.SignInAsync("bob", "dev", ct);
          var xsrf = await session.FetchXsrfTokenAsync(ct);
          using var req = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
              new { serverId = Guid.NewGuid(), sqlText = "UPDATE x", reason = "r" });
          var resp = await session.Client.SendAsync(req, ct);
          Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
      }

      [Fact]
      public async Task GetUpdates_Returns403_ForUserWithoutAnyUpdatePermission()
      {
          var ct = TestContext.Current.CancellationToken;
          using var session = await LoginHelper.SignInAsync("bob", "dev", ct);
          var resp = await session.Client.GetAsync("/api/update", ct);
          Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
      }

      // ── submit ───────────────────────────────────────────────────────────────

      [Fact]
      public async Task PostUpdate_Returns201_WithPendingStatus()
      {
          var ct = TestContext.Current.CancellationToken;
          var (session, serverId) = await AliceWithBlueServerAsync(
              [Permissions.UpdateSubmit], ct);
          using var _ = session;
          var xsrf = await session.FetchXsrfTokenAsync(ct);

          using var req = MutationRequest(HttpMethod.Post, "/api/update", xsrf, new
          {
              serverId,
              sqlText = "UPDATE public.users SET email = email WHERE 1=0",
              reason = "test submission",
          });
          var resp = await session.Client.SendAsync(req, ct);
          Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

          var detail = await resp.Content
              .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
          Assert.NotNull(detail);
          Assert.Equal("pending", detail.Status);
          Assert.Equal("test submission", detail.Reason);
          Assert.Null(detail.ReviewNote);
      }

      // ── list and get ─────────────────────────────────────────────────────────

      [Fact]
      public async Task GetUpdates_ReturnsList_ForUserWithSubmitPermission()
      {
          var ct = TestContext.Current.CancellationToken;
          var (session, serverId) = await AliceWithBlueServerAsync(
              [Permissions.UpdateSubmit], ct);
          using var _ = session;
          var xsrf = await session.FetchXsrfTokenAsync(ct);

          // Submit one first
          using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf, new
          {
              serverId,
              sqlText = "UPDATE public.users SET email = email WHERE 1=0",
              reason = "list test",
          });
          (await session.Client.SendAsync(submitReq, ct)).EnsureSuccessStatusCode();

          var resp = await session.Client.GetAsync("/api/update", ct);
          Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

          var list = await resp.Content
              .ReadFromJsonAsync<UpdateEndpoints.ListUpdateRequestsResponse>(ct);
          Assert.NotNull(list);
          Assert.NotEmpty(list.Requests);
      }

      // ── full happy path ──────────────────────────────────────────────────────

      [Fact]
      public async Task FullFlow_Pending_Approved_Executed()
      {
          var ct = TestContext.Current.CancellationToken;
          var (session, serverId) = await AliceWithBlueServerAsync(
              [Permissions.UpdateSubmit, Permissions.UpdateApprove, Permissions.UpdateExecute], ct);
          using var _ = session;
          var xsrf = await session.FetchXsrfTokenAsync(ct);

          // Submit
          using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf, new
          {
              serverId,
              sqlText = "UPDATE public.users SET email = email WHERE 1=0",
              reason = "happy path test",
          });
          var submitResp = await session.Client.SendAsync(submitReq, ct);
          submitResp.EnsureSuccessStatusCode();
          var submitted = await submitResp.Content
              .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
          Assert.Equal("pending", submitted!.Status);
          var requestId = submitted.Id;

          // Approve
          using var approveReq = MutationRequest(HttpMethod.Post,
              $"/api/update/{requestId}/approve", xsrf, new { note = "looks good" });
          var approveResp = await session.Client.SendAsync(approveReq, ct);
          approveResp.EnsureSuccessStatusCode();
          var approved = await approveResp.Content
              .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
          Assert.Equal("approved", approved!.Status);
          Assert.Equal("looks good", approved.ReviewNote);

          // Execute
          using var executeReq = MutationRequest(HttpMethod.Post,
              $"/api/update/{requestId}/execute", xsrf);
          var executeResp = await session.Client.SendAsync(executeReq, ct);
          executeResp.EnsureSuccessStatusCode();
          var executed = await executeResp.Content
              .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
          Assert.Equal("executed", executed!.Status);
          Assert.True(executed.ExecSuccess);
          Assert.NotNull(executed.ExecDurationMs);
          Assert.Null(executed.ExecError);
      }

      // ── state machine guards ─────────────────────────────────────────────────

      [Fact]
      public async Task Approve_Returns409_WhenAlreadyExecuted()
      {
          var ct = TestContext.Current.CancellationToken;
          var (session, serverId) = await AliceWithBlueServerAsync(
              [Permissions.UpdateSubmit, Permissions.UpdateApprove, Permissions.UpdateExecute], ct);
          using var _ = session;
          var xsrf = await session.FetchXsrfTokenAsync(ct);

          // Submit → Approve → Execute
          using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
              new { serverId, sqlText = "UPDATE public.users SET email = email WHERE 1=0", reason = "r" });
          var submitted = await (await session.Client.SendAsync(submitReq, ct)).Content
              .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
          var id = submitted!.Id;

          using var ar = MutationRequest(HttpMethod.Post, $"/api/update/{id}/approve", xsrf, new { note = "ok" });
          (await session.Client.SendAsync(ar, ct)).EnsureSuccessStatusCode();
          using var er = MutationRequest(HttpMethod.Post, $"/api/update/{id}/execute", xsrf);
          (await session.Client.SendAsync(er, ct)).EnsureSuccessStatusCode();

          // Try to approve again after execution
          using var ar2 = MutationRequest(HttpMethod.Post, $"/api/update/{id}/approve", xsrf, new { note = "again" });
          var resp = await session.Client.SendAsync(ar2, ct);
          Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
      }

      [Fact]
      public async Task Execute_Returns409_WhenStillPending()
      {
          var ct = TestContext.Current.CancellationToken;
          var (session, serverId) = await AliceWithBlueServerAsync(
              [Permissions.UpdateSubmit, Permissions.UpdateExecute], ct);
          using var _ = session;
          var xsrf = await session.FetchXsrfTokenAsync(ct);

          using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
              new { serverId, sqlText = "UPDATE public.users SET email = email WHERE 1=0", reason = "r" });
          var submitted = await (await session.Client.SendAsync(submitReq, ct)).Content
              .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
          var id = submitted!.Id;

          using var er = MutationRequest(HttpMethod.Post, $"/api/update/{id}/execute", xsrf);
          var resp = await session.Client.SendAsync(er, ct);
          Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
      }

      [Fact]
      public async Task Cancel_Returns409_WhenAlreadyRejected()
      {
          var ct = TestContext.Current.CancellationToken;
          var (session, serverId) = await AliceWithBlueServerAsync(
              [Permissions.UpdateSubmit, Permissions.UpdateApprove], ct);
          using var _ = session;
          var xsrf = await session.FetchXsrfTokenAsync(ct);

          using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
              new { serverId, sqlText = "UPDATE public.users SET email = email WHERE 1=0", reason = "r" });
          var submitted = await (await session.Client.SendAsync(submitReq, ct)).Content
              .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
          var id = submitted!.Id;

          using var rr = MutationRequest(HttpMethod.Post, $"/api/update/{id}/reject", xsrf, new { note = "no" });
          (await session.Client.SendAsync(rr, ct)).EnsureSuccessStatusCode();

          using var cr = MutationRequest(HttpMethod.Post, $"/api/update/{id}/cancel", xsrf);
          var resp = await session.Client.SendAsync(cr, ct);
          Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
      }

      [Fact]
      public async Task Cancel_Returns200_WhenApproved()
      {
          var ct = TestContext.Current.CancellationToken;
          var (session, serverId) = await AliceWithBlueServerAsync(
              [Permissions.UpdateSubmit, Permissions.UpdateApprove], ct);
          using var _ = session;
          var xsrf = await session.FetchXsrfTokenAsync(ct);

          using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
              new { serverId, sqlText = "UPDATE public.users SET email = email WHERE 1=0", reason = "r" });
          var submitted = await (await session.Client.SendAsync(submitReq, ct)).Content
              .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
          var id = submitted!.Id;

          using var ar = MutationRequest(HttpMethod.Post, $"/api/update/{id}/approve", xsrf, new { note = "ok" });
          (await session.Client.SendAsync(ar, ct)).EnsureSuccessStatusCode();

          using var cr = MutationRequest(HttpMethod.Post, $"/api/update/{id}/cancel", xsrf);
          var resp = await session.Client.SendAsync(cr, ct);
          Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

          var detail = await resp.Content
              .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
          Assert.Equal("cancelled", detail!.Status);
      }

      [Fact]
      public async Task Execute_MarksExecuted_EvenOnSqlError()
      {
          var ct = TestContext.Current.CancellationToken;
          var (session, serverId) = await AliceWithBlueServerAsync(
              [Permissions.UpdateSubmit, Permissions.UpdateApprove, Permissions.UpdateExecute], ct);
          using var _ = session;
          var xsrf = await session.FetchXsrfTokenAsync(ct);

          using var submitReq = MutationRequest(HttpMethod.Post, "/api/update", xsrf,
              new { serverId, sqlText = "UPDATE public.nonexistent SET foo = 'bar'", reason = "bad sql test" });
          var submitted = await (await session.Client.SendAsync(submitReq, ct)).Content
              .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
          var id = submitted!.Id;

          using var ar = MutationRequest(HttpMethod.Post, $"/api/update/{id}/approve", xsrf, new { note = "ok" });
          (await session.Client.SendAsync(ar, ct)).EnsureSuccessStatusCode();

          using var er = MutationRequest(HttpMethod.Post, $"/api/update/{id}/execute", xsrf);
          var resp = await session.Client.SendAsync(er, ct);
          Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

          var detail = await resp.Content
              .ReadFromJsonAsync<UpdateEndpoints.UpdateRequestDetailResponse>(ct);
          Assert.Equal("executed", detail!.Status);
          Assert.False(detail.ExecSuccess);
          Assert.NotNull(detail.ExecError);
      }
  }
  ```

- [ ] **Step 2: Run the tests**

  ```bash
  dotnet test tests/IntegrationTests/ --filter "FullyQualifiedName~UpdateEndpointTests"
  ```
  Expected: All tests pass. If any fail, examine output and fix the relevant endpoint.

- [ ] **Step 3: Run all integration tests to check for regressions**

  ```bash
  dotnet test tests/IntegrationTests/
  ```
  Expected: All tests pass.

- [ ] **Step 4: Commit**

  ```bash
  git add tests/IntegrationTests/UpdateEndpointTests.cs
  git commit -m "test: add UpdateEndpoint integration tests"
  ```

---

## Task 7: Regenerate OpenAPI schema

**Files:**
- Modify: `src/SluiceBase.Api/openapi.json` (auto-generated)
- Modify: `src/frontend/src/api/schema.ts` (auto-generated)

- [ ] **Step 1: Regenerate openapi.json**

  ```bash
  dotnet build src/SluiceBase.Api/SluiceBase.Api.csproj
  ```
  This triggers the `Microsoft.Extensions.ApiDescription.Server` generator. Verify `src/SluiceBase.Api/openapi.json` now contains paths for `/api/update`, `/api/update/{id}`, `/api/update/{id}/approve`, etc.

- [ ] **Step 2: Regenerate schema.ts**

  ```bash
  cd src/frontend && pnpm run generate-types
  ```
  Expected: `src/frontend/src/api/schema.ts` is updated with the new paths and types for the update endpoints.

  If the project uses a different command, check `src/frontend/package.json` for the script name:
  ```bash
  cat src/frontend/package.json | grep generate
  ```

- [ ] **Step 3: Commit**

  ```bash
  git add src/SluiceBase.Api/openapi.json src/frontend/src/api/schema.ts
  git commit -m "chore: regenerate OpenAPI schema with update endpoints"
  ```

---

## Task 8: Frontend hooks and hook unit tests

**Files:**
- Modify: `src/frontend/src/api/hooks.ts`
- Create: `src/frontend/src/api/__tests__/update-hooks.test.ts`

- [ ] **Step 1: Write failing hook tests**

  `src/frontend/src/api/__tests__/update-hooks.test.ts`:
  ```typescript
  import { renderHook, waitFor } from "@testing-library/react";
  import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
  import { beforeEach, describe, expect, it, vi } from "vitest";
  import React from "react";
  import {
    useUpdateRequests,
    useUpdateRequest,
    useSubmitUpdate,
    useApproveUpdate,
    useRejectUpdate,
    useCancelUpdate,
    useExecuteUpdate,
  } from "@/api/hooks";

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

  const { apiRequest } = await import("@/api/client");

  function wrapper({ children }: { children: React.ReactNode }) {
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    return React.createElement(QueryClientProvider, { client: qc }, children);
  }

  beforeEach(() => {
    vi.clearAllMocks();
  });

  const fakeList = {
    requests: [
      {
        id: "req-1",
        serverName: "Blue",
        submitterName: "Alice",
        reason: "fix data",
        status: "pending",
        submittedAt: "2026-05-09T00:00:00Z",
        execSuccess: null,
      },
    ],
  };

  const fakeDetail = {
    id: "req-1",
    serverId: "srv-1",
    serverName: "Blue",
    submitterId: "user-1",
    submitterName: "Alice",
    sqlText: "UPDATE public.users SET email = email WHERE 1=0",
    reason: "fix data",
    status: "pending",
    reviewerId: null,
    reviewerName: null,
    reviewNote: null,
    executorId: null,
    executorName: null,
    submittedAt: "2026-05-09T00:00:00Z",
    reviewedAt: null,
    executedAt: null,
    execSuccess: null,
    execDurationMs: null,
    execAffectedRows: null,
    execError: null,
  };

  describe("useUpdateRequests", () => {
    it("fetches GET /api/update", async () => {
      vi.mocked(apiRequest).mockResolvedValue(fakeList);
      const { result } = renderHook(() => useUpdateRequests(), { wrapper });
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(apiRequest).toHaveBeenCalledWith("/api/update");
      expect(result.current.data?.requests).toHaveLength(1);
    });
  });

  describe("useUpdateRequest", () => {
    it("fetches GET /api/update/:id", async () => {
      vi.mocked(apiRequest).mockResolvedValue(fakeDetail);
      const { result } = renderHook(() => useUpdateRequest("req-1"), { wrapper });
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(apiRequest).toHaveBeenCalledWith("/api/update/req-1");
      expect(result.current.data?.status).toBe("pending");
    });
  });

  describe("useSubmitUpdate", () => {
    it("posts to /api/update", async () => {
      vi.mocked(apiRequest).mockResolvedValue(fakeDetail);
      const { result } = renderHook(() => useSubmitUpdate(), { wrapper });
      result.current.mutate({
        serverId: "srv-1",
        sqlText: "UPDATE public.users SET email = email WHERE 1=0",
        reason: "fix data",
      });
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(apiRequest).toHaveBeenCalledWith(
        "/api/update",
        expect.objectContaining({ method: "POST" }),
      );
    });
  });

  describe("useApproveUpdate", () => {
    it("posts to /api/update/:id/approve", async () => {
      vi.mocked(apiRequest).mockResolvedValue({ ...fakeDetail, status: "approved" });
      const { result } = renderHook(() => useApproveUpdate(), { wrapper });
      result.current.mutate({ id: "req-1", note: "looks good" });
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(apiRequest).toHaveBeenCalledWith(
        "/api/update/req-1/approve",
        expect.objectContaining({ method: "POST", body: { note: "looks good" } }),
      );
    });
  });

  describe("useRejectUpdate", () => {
    it("posts to /api/update/:id/reject", async () => {
      vi.mocked(apiRequest).mockResolvedValue({ ...fakeDetail, status: "rejected" });
      const { result } = renderHook(() => useRejectUpdate(), { wrapper });
      result.current.mutate({ id: "req-1", note: "not safe" });
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(apiRequest).toHaveBeenCalledWith(
        "/api/update/req-1/reject",
        expect.objectContaining({ method: "POST", body: { note: "not safe" } }),
      );
    });
  });

  describe("useCancelUpdate", () => {
    it("posts to /api/update/:id/cancel", async () => {
      vi.mocked(apiRequest).mockResolvedValue({ ...fakeDetail, status: "cancelled" });
      const { result } = renderHook(() => useCancelUpdate(), { wrapper });
      result.current.mutate("req-1");
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(apiRequest).toHaveBeenCalledWith(
        "/api/update/req-1/cancel",
        expect.objectContaining({ method: "POST" }),
      );
    });
  });

  describe("useExecuteUpdate", () => {
    it("posts to /api/update/:id/execute", async () => {
      vi.mocked(apiRequest).mockResolvedValue({
        ...fakeDetail,
        status: "executed",
        execSuccess: true,
        execDurationMs: 42,
        execAffectedRows: 0,
      });
      const { result } = renderHook(() => useExecuteUpdate(), { wrapper });
      result.current.mutate("req-1");
      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(apiRequest).toHaveBeenCalledWith(
        "/api/update/req-1/execute",
        expect.objectContaining({ method: "POST" }),
      );
      expect(result.current.data?.execSuccess).toBe(true);
    });
  });
  ```

- [ ] **Step 2: Run tests to verify they fail**

  ```bash
  cd src/frontend && pnpm test run src/api/__tests__/update-hooks.test.ts
  ```
  Expected: FAIL — hooks are not exported from `hooks.ts` yet.

- [ ] **Step 3: Add 7 hooks to hooks.ts**

  In `src/frontend/src/api/hooks.ts`, add after the `useExecuteQuery` hook:

  ```typescript
  // ── Update requests ───────────────────────────────────────────────────────

  export type UpdateSummaryItem =
    paths["/api/update"]["get"]["responses"][200]["content"]["application/json"]["requests"][0];
  export type UpdateRequestListResponse =
    paths["/api/update"]["get"]["responses"][200]["content"]["application/json"];
  export type UpdateRequestDetail =
    paths["/api/update/{id}"]["get"]["responses"][200]["content"]["application/json"];
  export type SubmitUpdateRequest =
    paths["/api/update"]["post"]["requestBody"]["content"]["application/json"];

  export function useUpdateRequests() {
    return useQuery({
      queryKey: ["update"] as const,
      queryFn: () => apiRequest<void, UpdateRequestListResponse>("/api/update"),
    });
  }

  export function useUpdateRequest(id: string) {
    return useQuery({
      queryKey: ["update", id] as const,
      queryFn: () => apiRequest<void, UpdateRequestDetail>(`/api/update/${id}`),
    });
  }

  export function useSubmitUpdate() {
    const qc = useQueryClient();
    return useMutation({
      mutationFn: (body: SubmitUpdateRequest) =>
        apiRequest<SubmitUpdateRequest, UpdateRequestDetail>("/api/update", {
          method: "POST",
          body,
        }),
      onSuccess: () => {
        void qc.invalidateQueries({ queryKey: ["update"] });
        notifications.show({ title: "Request submitted", message: "", color: "teal" });
      },
      onError: (error) => {
        notifications.show({
          title: "Submit failed",
          message: error instanceof ApiError ? formatApiError(error) : error.message,
          color: "red",
        });
      },
    });
  }

  export function useApproveUpdate() {
    const qc = useQueryClient();
    return useMutation({
      mutationFn: ({ id, note }: { id: string; note: string }) =>
        apiRequest<{ note: string }, UpdateRequestDetail>(`/api/update/${id}/approve`, {
          method: "POST",
          body: { note },
        }),
      onSuccess: (data) => {
        void qc.invalidateQueries({ queryKey: ["update"] });
        void qc.invalidateQueries({ queryKey: ["update", data.id] });
        notifications.show({ title: "Request approved", message: "", color: "teal" });
      },
      onError: (error) => {
        notifications.show({
          title: "Approve failed",
          message: error instanceof ApiError ? formatApiError(error) : error.message,
          color: "red",
        });
      },
    });
  }

  export function useRejectUpdate() {
    const qc = useQueryClient();
    return useMutation({
      mutationFn: ({ id, note }: { id: string; note: string }) =>
        apiRequest<{ note: string }, UpdateRequestDetail>(`/api/update/${id}/reject`, {
          method: "POST",
          body: { note },
        }),
      onSuccess: (data) => {
        void qc.invalidateQueries({ queryKey: ["update"] });
        void qc.invalidateQueries({ queryKey: ["update", data.id] });
        notifications.show({ title: "Request rejected", message: "", color: "red" });
      },
      onError: (error) => {
        notifications.show({
          title: "Reject failed",
          message: error instanceof ApiError ? formatApiError(error) : error.message,
          color: "red",
        });
      },
    });
  }

  export function useCancelUpdate() {
    const qc = useQueryClient();
    return useMutation({
      mutationFn: (id: string) =>
        apiRequest<void, UpdateRequestDetail>(`/api/update/${id}/cancel`, { method: "POST" }),
      onSuccess: (data) => {
        void qc.invalidateQueries({ queryKey: ["update"] });
        void qc.invalidateQueries({ queryKey: ["update", data.id] });
        notifications.show({ title: "Request cancelled", message: "", color: "gray" });
      },
      onError: (error) => {
        notifications.show({
          title: "Cancel failed",
          message: error instanceof ApiError ? formatApiError(error) : error.message,
          color: "red",
        });
      },
    });
  }

  export function useExecuteUpdate() {
    const qc = useQueryClient();
    return useMutation({
      mutationFn: (id: string) =>
        apiRequest<void, UpdateRequestDetail>(`/api/update/${id}/execute`, { method: "POST" }),
      onSuccess: (data) => {
        void qc.invalidateQueries({ queryKey: ["update"] });
        void qc.invalidateQueries({ queryKey: ["update", data.id] });
        notifications.show({ title: "Executed", message: "", color: "teal" });
      },
      onError: (error) => {
        notifications.show({
          title: "Execute failed",
          message: error instanceof ApiError ? formatApiError(error) : error.message,
          color: "red",
        });
      },
    });
  }
  ```

- [ ] **Step 4: Run tests to verify they pass**

  ```bash
  cd src/frontend && pnpm test run src/api/__tests__/update-hooks.test.ts
  ```
  Expected: All 7 describe blocks pass.

- [ ] **Step 5: Run full frontend test suite**

  ```bash
  cd src/frontend && pnpm test run
  ```
  Expected: All tests pass.

- [ ] **Step 6: Commit**

  ```bash
  git add src/frontend/src/api/hooks.ts src/frontend/src/api/__tests__/update-hooks.test.ts
  git commit -m "feat: add 7 update request hooks with tests"
  ```

---

## Task 9: Frontend list page (`/update`)

**Files:**
- Create: `src/frontend/src/routes/_authed/update/index.tsx`

- [ ] **Step 1: Create the update directory and list page**

  Create directory `src/frontend/src/routes/_authed/update/` then create `index.tsx`:

  ```tsx
  import {
    Badge,
    Button,
    Group,
    Stack,
    Table,
    Text,
    Title,
  } from "@mantine/core";
  import { IconPlus } from "@tabler/icons-react";
  import { createFileRoute, Link, redirect, useNavigate } from "@tanstack/react-router";
  import { meQueryOptions, useUpdateRequests } from "@/api/hooks";

  export const Route = createFileRoute("/_authed/update/")({
    beforeLoad: ({ context }) => {
      const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
      const hasAny =
        me?.permissions.includes("update:submit") ||
        me?.permissions.includes("update:approve") ||
        me?.permissions.includes("update:execute");
      if (!hasAny) {
        throw redirect({ to: "/" });
      }
    },
    component: UpdateListPage,
  });

  const STATUS_COLOR: Record<string, string> = {
    pending: "blue",
    approved: "green",
    rejected: "red",
    cancelled: "gray",
    executed: "teal",
  };

  function statusBadge(status: string, execSuccess?: boolean | null) {
    if (status === "executed" && execSuccess === false) {
      return <Badge color="red">Failed</Badge>;
    }
    return <Badge color={STATUS_COLOR[status] ?? "gray"}>{status}</Badge>;
  }

  function UpdateListPage() {
    const requests = useUpdateRequests();
    const navigate = useNavigate();
    const me = (Route.useRouteContext() as { queryClient: import("@tanstack/react-query").QueryClient })
      .queryClient.getQueryData(meQueryOptions.queryKey);
    const canSubmit = me?.permissions.includes("update:submit") ?? false;

    return (
      <Stack gap="md">
        <Group justify="space-between">
          <Title order={2}>Update Requests</Title>
          {canSubmit && (
            <Button
              leftSection={<IconPlus size={14} />}
              component={Link}
              to="/update/new"
            >
              New Request
            </Button>
          )}
        </Group>

        {requests.isPending && <Text c="dimmed">Loading…</Text>}
        {requests.isError && <Text c="red">Failed to load requests.</Text>}
        {requests.data && requests.data.requests.length === 0 && (
          <Text c="dimmed">No update requests yet.</Text>
        )}

        {requests.data && requests.data.requests.length > 0 && (
          <Table striped withTableBorder highlightOnHover style={{ cursor: "pointer" }}>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Status</Table.Th>
                <Table.Th>Server</Table.Th>
                <Table.Th>Submitted by</Table.Th>
                <Table.Th>Reason</Table.Th>
                <Table.Th>Submitted</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {requests.data.requests.map((r) => (
                <Table.Tr
                  key={r.id}
                  onClick={() => void navigate({ to: "/update/$id", params: { id: r.id } })}
                >
                  <Table.Td>{statusBadge(r.status, r.execSuccess)}</Table.Td>
                  <Table.Td>{r.serverName ?? "—"}</Table.Td>
                  <Table.Td>{r.submitterName ?? "—"}</Table.Td>
                  <Table.Td style={{ maxWidth: 300, overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
                    {r.reason}
                  </Table.Td>
                  <Table.Td>
                    {new Date(r.submittedAt).toLocaleString()}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Stack>
    );
  }
  ```

- [ ] **Step 2: Trigger route tree regeneration**

  Start the dev server briefly to let TanStack Router regenerate `routeTree.gen.ts`, then stop it:
  ```bash
  cd src/frontend && pnpm dev
  ```
  Wait for `VITE ready` message, then Ctrl+C. Alternatively run:
  ```bash
  cd src/frontend && pnpm build
  ```
  Verify `src/frontend/src/routeTree.gen.ts` now contains `"/_authed/update/"`.

- [ ] **Step 3: Type-check**

  ```bash
  cd src/frontend && pnpm tsc --noEmit
  ```
  Expected: No errors.

- [ ] **Step 4: Commit**

  ```bash
  git add src/frontend/src/routes/_authed/update/
  git commit -m "feat: add update request list page"
  ```

---

## Task 10: Frontend new request form (`/update/new`)

**Files:**
- Create: `src/frontend/src/routes/_authed/update/new.tsx`

- [ ] **Step 1: Create new.tsx**

  `src/frontend/src/routes/_authed/update/new.tsx`:
  ```tsx
  import {
    Box,
    Button,
    Group,
    Select,
    Stack,
    Text,
    Textarea,
    Title,
  } from "@mantine/core";
  import { createFileRoute, redirect, useNavigate } from "@tanstack/react-router";
  import { useState } from "react";
  import CodeMirror from "@uiw/react-codemirror";
  import { sql } from "@codemirror/lang-sql";
  import { githubDark, githubLight } from "@uiw/codemirror-themes-all";
  import { useMantineColorScheme } from "@mantine/core";
  import { meQueryOptions, useServers, useSubmitUpdate } from "@/api/hooks";

  export const Route = createFileRoute("/_authed/update/new")({
    beforeLoad: ({ context }) => {
      const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
      if (!me?.permissions.includes("update:submit")) {
        throw redirect({ to: "/" });
      }
    },
    component: NewUpdatePage,
  });

  function NewUpdatePage() {
    const navigate = useNavigate();
    const servers = useServers();
    const submit = useSubmitUpdate();
    const { colorScheme } = useMantineColorScheme();

    const [serverId, setServerId] = useState<string | null>(null);
    const [sqlText, setSqlText] = useState("");
    const [reason, setReason] = useState("");

    const serverOptions = (servers.data?.servers ?? [])
      .filter((s) => s.hasWriteCredential && s.isEnabled)
      .map((s) => ({ value: s.id, label: s.name }));

    const canSubmit = serverId !== null && sqlText.trim() !== "" && reason.trim() !== "";

    function handleSubmit() {
      if (!canSubmit) return;
      submit.mutate(
        { serverId, sqlText, reason },
        {
          onSuccess: (data) => {
            void navigate({ to: "/update/$id", params: { id: data.id } });
          },
        },
      );
    }

    return (
      <Stack gap="md">
        <Title order={2}>New Update Request</Title>

        <Select
          label="Server"
          placeholder="Select a server with write credentials"
          data={serverOptions}
          value={serverId}
          onChange={setServerId}
          required
        />

        <Box>
          <Text size="sm" fw={500} mb={4}>
            SQL <Text span c="red">*</Text>
          </Text>
          <Box
            style={{
              border: "1px solid var(--mantine-color-default-border)",
              borderRadius: "var(--mantine-radius-sm)",
              overflow: "hidden",
            }}
          >
            <CodeMirror
              value={sqlText}
              onChange={setSqlText}
              extensions={[sql()]}
              theme={colorScheme === "dark" ? githubDark : githubLight}
              height="300px"
              basicSetup={{ lineNumbers: true, foldGutter: false }}
            />
          </Box>
        </Box>

        <Textarea
          label="Reason"
          description="Describe why this change is needed. A ticket link is fine."
          placeholder="e.g. https://example.com/ticket/... — fixing bad email for user X"
          required
          minRows={3}
          value={reason}
          onChange={(e) => setReason(e.currentTarget.value)}
        />

        <Group>
          <Button onClick={handleSubmit} loading={submit.isPending} disabled={!canSubmit}>
            Submit for Approval
          </Button>
          <Button variant="subtle" component="a" href="/update">
            Cancel
          </Button>
        </Group>
      </Stack>
    );
  }
  ```

- [ ] **Step 2: Type-check**

  ```bash
  cd src/frontend && pnpm tsc --noEmit
  ```
  Expected: No errors.

- [ ] **Step 3: Commit**

  ```bash
  git add src/frontend/src/routes/_authed/update/new.tsx
  git commit -m "feat: add new update request form"
  ```

---

## Task 11: Frontend detail page (`/update/$id`)

**Files:**
- Create: `src/frontend/src/routes/_authed/update/$id.tsx`

- [ ] **Step 1: Create $id.tsx**

  `src/frontend/src/routes/_authed/update/$id.tsx`:
  ```tsx
  import {
    Alert,
    Badge,
    Box,
    Button,
    Group,
    Modal,
    Paper,
    Skeleton,
    Stack,
    Text,
    Textarea,
    Title,
  } from "@mantine/core";
  import { useDisclosure } from "@mantine/hooks";
  import { modals } from "@mantine/modals";
  import { createFileRoute, redirect } from "@tanstack/react-router";
  import { useState } from "react";
  import CodeMirror from "@uiw/react-codemirror";
  import { sql } from "@codemirror/lang-sql";
  import { githubDark, githubLight } from "@uiw/codemirror-themes-all";
  import { useMantineColorScheme } from "@mantine/core";
  import {
    meQueryOptions,
    useApproveUpdate,
    useCancelUpdate,
    useExecuteUpdate,
    useRejectUpdate,
    useUpdateRequest,
  } from "@/api/hooks";

  export const Route = createFileRoute("/_authed/update/$id")({
    beforeLoad: ({ context }) => {
      const me = context.queryClient.getQueryData(meQueryOptions.queryKey);
      const hasAny =
        me?.permissions.includes("update:submit") ||
        me?.permissions.includes("update:approve") ||
        me?.permissions.includes("update:execute");
      if (!hasAny) {
        throw redirect({ to: "/" });
      }
    },
    component: UpdateDetailPage,
  });

  const STATUS_COLOR: Record<string, string> = {
    pending: "blue",
    approved: "green",
    rejected: "red",
    cancelled: "gray",
    executed: "teal",
  };

  function UpdateDetailPage() {
    const { id } = Route.useParams();
    const meData = (Route.useRouteContext() as { queryClient: import("@tanstack/react-query").QueryClient })
      .queryClient.getQueryData(meQueryOptions.queryKey);
    const request = useUpdateRequest(id);
    const approve = useApproveUpdate();
    const reject = useRejectUpdate();
    const cancel = useCancelUpdate();
    const execute = useExecuteUpdate();
    const { colorScheme } = useMantineColorScheme();

    const [approveModalOpen, { open: openApprove, close: closeApprove }] = useDisclosure(false);
    const [rejectModalOpen, { open: openReject, close: closeReject }] = useDisclosure(false);
    const [reviewNote, setReviewNote] = useState("");

    const canApprove = meData?.permissions.includes("update:approve") ?? false;
    const canSubmit = meData?.permissions.includes("update:submit") ?? false;
    const canExecute = meData?.permissions.includes("update:execute") ?? false;

    if (request.isPending) {
      return (
        <Stack gap="md">
          {[1, 2, 3].map((i) => <Skeleton key={i} h={60} radius="sm" />)}
        </Stack>
      );
    }

    if (request.isError || !request.data) {
      return <Alert color="red" title="Not found">Request not found or could not be loaded.</Alert>;
    }

    const r = request.data;

    function handleApprove() {
      if (!reviewNote.trim()) return;
      approve.mutate(
        { id, note: reviewNote },
        { onSuccess: () => { closeApprove(); setReviewNote(""); } },
      );
    }

    function handleReject() {
      if (!reviewNote.trim()) return;
      reject.mutate(
        { id, note: reviewNote },
        { onSuccess: () => { closeReject(); setReviewNote(""); } },
      );
    }

    function handleCancel() {
      modals.openConfirmModal({
        title: "Cancel request",
        children: <Text>Are you sure you want to cancel this update request?</Text>,
        labels: { confirm: "Cancel request", cancel: "Keep it" },
        confirmProps: { color: "red" },
        onConfirm: () => cancel.mutate(id),
      });
    }

    function handleExecute() {
      modals.openConfirmModal({
        title: "Execute update",
        children: (
          <Text>
            This will run the SQL against the write connection. This cannot be undone. Proceed?
          </Text>
        ),
        labels: { confirm: "Execute", cancel: "Go back" },
        confirmProps: { color: "green" },
        onConfirm: () => execute.mutate(id),
      });
    }

    const execBadge = r.status === "executed"
      ? r.execSuccess
        ? <Badge color="teal">Succeeded</Badge>
        : <Badge color="red">Failed</Badge>
      : null;

    return (
      <Stack gap="md">
        <Group gap="sm">
          <Title order={2}>Update Request</Title>
          <Badge color={STATUS_COLOR[r.status] ?? "gray"} size="lg">{r.status}</Badge>
        </Group>

        {/* SQL */}
        <Box
          style={{
            border: "1px solid var(--mantine-color-default-border)",
            borderRadius: "var(--mantine-radius-sm)",
            overflow: "hidden",
          }}
        >
          <CodeMirror
            value={r.sqlText}
            extensions={[sql()]}
            theme={colorScheme === "dark" ? githubDark : githubLight}
            height="200px"
            basicSetup={{ lineNumbers: true, foldGutter: false }}
            editable={false}
          />
        </Box>

        {/* Metadata */}
        <Paper withBorder p="md">
          <Stack gap="xs">
            <Group gap="xs">
              <Text size="sm" fw={500}>Server:</Text>
              <Text size="sm">{r.serverName ?? "—"}</Text>
            </Group>
            <Group gap="xs">
              <Text size="sm" fw={500}>Submitted by:</Text>
              <Text size="sm">{r.submitterName ?? "—"}</Text>
            </Group>
            <Group gap="xs">
              <Text size="sm" fw={500}>Submitted at:</Text>
              <Text size="sm">{new Date(r.submittedAt).toLocaleString()}</Text>
            </Group>
            <Group gap="xs" align="flex-start">
              <Text size="sm" fw={500}>Reason:</Text>
              <Text size="sm" style={{ flex: 1 }}>{r.reason}</Text>
            </Group>
          </Stack>
        </Paper>

        {/* Review section */}
        {r.status !== "pending" && r.reviewedAt && (
          <Paper withBorder p="md">
            <Stack gap="xs">
              <Text size="sm" fw={600}>Review</Text>
              <Group gap="xs">
                <Text size="sm" fw={500}>{r.status === "rejected" ? "Rejected by:" : "Approved by:"}</Text>
                <Text size="sm">{r.reviewerName ?? "—"}</Text>
              </Group>
              <Group gap="xs">
                <Text size="sm" fw={500}>At:</Text>
                <Text size="sm">{new Date(r.reviewedAt).toLocaleString()}</Text>
              </Group>
              {r.reviewNote && (
                <Group gap="xs" align="flex-start">
                  <Text size="sm" fw={500}>Note:</Text>
                  <Text size="sm" style={{ flex: 1 }}>{r.reviewNote}</Text>
                </Group>
              )}
            </Stack>
          </Paper>
        )}

        {/* Execution section */}
        {r.status === "executed" && r.executedAt && (
          <Paper withBorder p="md">
            <Stack gap="xs">
              <Group gap="xs">
                <Text size="sm" fw={600}>Execution</Text>
                {execBadge}
              </Group>
              <Group gap="xs">
                <Text size="sm" fw={500}>Executed by:</Text>
                <Text size="sm">{r.executorName ?? "—"}</Text>
              </Group>
              <Group gap="xs">
                <Text size="sm" fw={500}>At:</Text>
                <Text size="sm">{new Date(r.executedAt).toLocaleString()}</Text>
              </Group>
              {r.execDurationMs != null && (
                <Group gap="xs">
                  <Text size="sm" fw={500}>Duration:</Text>
                  <Text size="sm">{r.execDurationMs} ms</Text>
                </Group>
              )}
              {r.execAffectedRows != null && (
                <Group gap="xs">
                  <Text size="sm" fw={500}>Affected rows:</Text>
                  <Text size="sm">{r.execAffectedRows}</Text>
                </Group>
              )}
              {r.execError && (
                <Alert color="red" title="Error">{r.execError}</Alert>
              )}
            </Stack>
          </Paper>
        )}

        {/* Action area */}
        <Group>
          {r.status === "pending" && canApprove && (
            <>
              <Button color="green" onClick={openApprove} loading={approve.isPending}>
                Approve
              </Button>
              <Button color="red" variant="outline" onClick={openReject} loading={reject.isPending}>
                Reject
              </Button>
            </>
          )}
          {(r.status === "pending" || r.status === "approved") && canSubmit && (
            <Button color="gray" variant="outline" onClick={handleCancel} loading={cancel.isPending}>
              Cancel
            </Button>
          )}
          {r.status === "approved" && canExecute && (
            <Button color="teal" onClick={handleExecute} loading={execute.isPending}>
              Execute
            </Button>
          )}
        </Group>

        {/* Approve modal */}
        <Modal opened={approveModalOpen} onClose={closeApprove} title="Approve request">
          <Stack gap="md">
            <Textarea
              label="Note"
              description="Required — describe why you're approving."
              placeholder="Looks correct, verified against staging."
              required
              minRows={3}
              value={reviewNote}
              onChange={(e) => setReviewNote(e.currentTarget.value)}
            />
            <Group justify="flex-end">
              <Button variant="subtle" onClick={closeApprove}>Back</Button>
              <Button
                color="green"
                onClick={handleApprove}
                disabled={!reviewNote.trim()}
                loading={approve.isPending}
              >
                Confirm Approve
              </Button>
            </Group>
          </Stack>
        </Modal>

        {/* Reject modal */}
        <Modal opened={rejectModalOpen} onClose={closeReject} title="Reject request">
          <Stack gap="md">
            <Textarea
              label="Note"
              description="Required — describe why you're rejecting."
              placeholder="SQL targets wrong table, needs revision."
              required
              minRows={3}
              value={reviewNote}
              onChange={(e) => setReviewNote(e.currentTarget.value)}
            />
            <Group justify="flex-end">
              <Button variant="subtle" onClick={closeReject}>Back</Button>
              <Button
                color="red"
                onClick={handleReject}
                disabled={!reviewNote.trim()}
                loading={reject.isPending}
              >
                Confirm Reject
              </Button>
            </Group>
          </Stack>
        </Modal>
      </Stack>
    );
  }
  ```

- [ ] **Step 2: Type-check**

  ```bash
  cd src/frontend && pnpm tsc --noEmit
  ```
  Expected: No errors.

- [ ] **Step 3: Commit**

  ```bash
  git add src/frontend/src/routes/_authed/update/\$id.tsx
  git commit -m "feat: add update request detail page"
  ```

---

## Task 12: Nav link and final verification

**Files:**
- Modify: `src/frontend/src/routes/_authed.tsx`

- [ ] **Step 1: Add Updates nav link to _authed.tsx**

  In `src/frontend/src/routes/_authed.tsx`, add the icon import:
  ```tsx
  import { IconArrowsExchange } from "@tabler/icons-react";
  ```

  Add three permission checks after `const canQuery = useHasPermission("query:execute");`:
  ```tsx
  const canSubmitUpdates = useHasPermission("update:submit");
  const canApproveUpdates = useHasPermission("update:approve");
  const canExecuteUpdates = useHasPermission("update:execute");
  const canSeeUpdates = canSubmitUpdates || canApproveUpdates || canExecuteUpdates;
  ```

  Add the nav link after the Query link:
  ```tsx
  {canSeeUpdates && (
    <NavLink
      label="Updates"
      leftSection={<IconArrowsExchange size={16} />}
      component={Link}
      to="/update"
      active={location.pathname.startsWith("/update")}
    />
  )}
  ```

- [ ] **Step 2: Trigger route tree regeneration**

  ```bash
  cd src/frontend && pnpm build
  ```
  Expected: Build succeeds. Verify `routeTree.gen.ts` contains `"/_authed/update/"`, `"/_authed/update/new"`, `"/_authed/update/$id"`.

- [ ] **Step 3: Run full test suite**

  ```bash
  cd src/frontend && pnpm test run
  ```
  Expected: All tests pass.

  ```bash
  dotnet test tests/IntegrationTests/
  ```
  Expected: All tests pass.

- [ ] **Step 4: Commit**

  ```bash
  git add src/frontend/src/routes/_authed.tsx src/frontend/src/routeTree.gen.ts
  git commit -m "feat: add Updates nav link to authenticated layout"
  ```
