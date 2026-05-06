using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Endpoints;

internal static class ServerEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var servers = app.MapGroup("/api/server")
            .RequireAuthorization(Permissions.ServerManage);

        servers.MapGet("/", ListServers).WithName("ListServers");
        servers.MapPost("/", CreateServer).WithName("CreateServer");
        servers.MapPut("/{id}", UpdateServer).WithName("UpdateServer");
        servers.MapDelete("/{id}", DeleteServer).WithName("DeleteServer");
        servers.MapPost("/{id}/test", TestConnection).WithName("TestConnection");
    }

    // ── list ─────────────────────────────────────────────────────────────────

    private static async Task<Ok<ListServersResponse>> ListServers(
        AppDbContext db, CancellationToken ct)
    {
        var servers = await db.Servers
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => ToResponse(s))
            .ToListAsync(ct);
        return TypedResults.Ok(new ListServersResponse(servers));
    }

    // ── create ────────────────────────────────────────────────────────────────

    private static async Task<Results<Created<ServerResponse>, ValidationProblem, Conflict>> CreateServer(
        CreateServerRequest req,
        AppDbContext db,
        IDataProtectionProvider dataProtection,
        TimeProvider clock,
        CancellationToken ct)
    {
        var validationErrors = ValidateWriteCredentials(req.WriteUsername, req.WritePassword);
        if (validationErrors is not null)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        var protector = dataProtection.CreateProtector(ServerConnectionFactory.ProtectorPurpose);
        var encReadPass = protector.Protect(req.ReadPassword);
        var encWritePass = req.WritePassword is not null ? protector.Protect(req.WritePassword) : null;

        var server = Server.Create(
            req.Name,
            req.Kind,
            req.Host,
            req.Port,
            req.Database,
            req.ReadUsername,
            encReadPass,
            req.WriteUsername,
            encWritePass,
            clock.GetUtcNow());

        db.Servers.Add(server);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique") == true ||
                                           ex.InnerException?.Message.Contains("duplicate") == true)
        {
            return TypedResults.Conflict();
        }

        return TypedResults.Created($"/api/server/{server.Id}", ToResponse(server));
    }

    // ── update ────────────────────────────────────────────────────────────────

    private static async Task<Results<Ok<ServerResponse>, ValidationProblem, NotFound>> UpdateServer(
        ServerId id,
        UpdateServerRequest request,
        AppDbContext db,
        IDataProtectionProvider dataProtection,
        TimeProvider clock,
        CancellationToken ct)
    {
        var server = await db.Servers.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (server is null)
        {
            return TypedResults.NotFound();
        }

        var protector = dataProtection.CreateProtector(ServerConnectionFactory.ProtectorPurpose);
        var now = clock.GetUtcNow();

        server.Update(request.Name, request.Host, request.Port, request.Database, request.ReadUsername, request.IsEnabled, now);

        if (request.ReadPassword is not null)
        {
            server.ReplaceReadPassword(protector.Protect(request.ReadPassword), now);
        }

        if (request.WriteUsername == string.Empty || request.WritePassword == string.Empty)
        {
            server.ClearWriteCredential(now);
        }

        if (request.WriteUsername is not null && request.WritePassword is not null)
        {
            var validationErrors = ValidateWriteCredentials(request.WriteUsername, request.WritePassword);
            if (validationErrors is not null)
            {
                return TypedResults.ValidationProblem(validationErrors);
            }

            server.SetWriteCredential(request.WriteUsername, protector.Protect(request.WritePassword), now);
        }
        // Both null → keep existing write credential unchanged

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(server));
    }

    // ── delete ────────────────────────────────────────────────────────────────

    private static async Task<Results<NoContent, NotFound>> DeleteServer(
        ServerId id,
        AppDbContext db,
        CancellationToken ct)
    {
        var server = await db.Servers.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (server is null)
        {
            return TypedResults.NotFound();
        }

        db.Servers.Remove(server);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ── test connection ───────────────────────────────────────────────────────

    private static async Task<Results<Ok<TestConnectionResponse>, NotFound>> TestConnection(
        ServerId id,
        AppDbContext db,
        IDataProtectionProvider dataProtection,
        ITargetEngine targetEngine,
        IServerConnectionFactory factory,
        CancellationToken ct)
    {
        var server = await db.Servers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, ct);
        if (server is null)
        {
            return TypedResults.NotFound();
        }

        var connectionString = await factory.GetConnectionStringAsync(server.Id, CredentialKind.Read, ct);
        var readResult = await targetEngine.TestConnectionAsync(connectionString, ct);

        ConnectivityResult? writeResult = null;
        if (server.HasWriteCredential)
        {
            var writeConnectionString = await factory.GetConnectionStringAsync(server.Id, CredentialKind.Write, ct);
            writeResult = await targetEngine.TestConnectionAsync(writeConnectionString, ct);
        }

        return TypedResults.Ok(new TestConnectionResponse(readResult, writeResult));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Dictionary<string, string[]>? ValidateWriteCredentials(
        string? username, string? password)
    {
        var hasUser = !string.IsNullOrEmpty(username);
        var hasPass = !string.IsNullOrEmpty(password);
        if (hasUser == hasPass)
        {
            return null;
        }

        return new Dictionary<string, string[]>
        {
            ["writeCredentials"] = ["WriteUsername and WritePassword must both be provided or both omitted."]
        };
    }

    private static ServerResponse ToResponse(Server s) =>
        new(s.Id,
            s.Name,
            s.Kind,
            s.Host,
            s.Port,
            s.Database,
            s.IsEnabled,
            s.HasWriteCredential,
            s.CreatedAt,
            s.UpdatedAt);

    // ── request / response records ────────────────────────────────────────────

    public sealed record ListServersResponse(IReadOnlyList<ServerResponse> Servers);

    public sealed record ServerResponse(
        ServerId Id,
        string Name,
        string Kind,
        string Host,
        int Port,
        string Database,
        bool IsEnabled,
        bool HasWriteCredential,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record CreateServerRequest(
        string Name,
        string Kind,
        string Host,
        int Port,
        string Database,
        string ReadUsername,
        string ReadPassword,
        string? WriteUsername = null,
        string? WritePassword = null);

    public sealed record UpdateServerRequest(
        string Name,
        string Host,
        int Port,
        string Database,
        string? ReadUsername = null,
        string? ReadPassword = null,
        string? WriteUsername = null,
        string? WritePassword = null,
        bool IsEnabled = true);

    public sealed record TestConnectionResponse(
        ConnectivityResult Read,
        ConnectivityResult? Write);
}