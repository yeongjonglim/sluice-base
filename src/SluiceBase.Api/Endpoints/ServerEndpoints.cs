// src/SluiceBase.Api/Endpoints/ServerEndpoints.cs
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;

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
    }

    // ── list ─────────────────────────────────────────────────────────────────

    private static async Task<Ok<ListServersResponse>> ListServers(
        AppDbContext db, CancellationToken ct)
    {
        var servers = await db.Servers
            .AsNoTracking()
            .Where(s => s.DeletedAt == null)
            .Include(s => s.Credentials.Where(c => c.DeletedAt == null))
            .Include(s => s.Databases.Where(d => d.DeletedAt == null))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
        return TypedResults.Ok(new ListServersResponse([.. servers.Select(ToResponse)]));
    }

    // ── create ────────────────────────────────────────────────────────────────

    private static async Task<Results<Created<ServerResponse>, Conflict>> CreateServer(
        CreateServerRequest req,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var server = Server.Create(req.Name, req.Kind, req.Host, req.Port, clock.GetUtcNow());
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

    private static async Task<Results<Ok<ServerResponse>, NotFound, Conflict>> UpdateServer(
        ServerId id,
        UpdateServerRequest req,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var server = await db.Servers
            .Include(s => s.Credentials.Where(c => c.DeletedAt == null))
            .Include(s => s.Databases.Where(d => d.DeletedAt == null))
            .SingleOrDefaultAsync(s => s.Id == id && s.DeletedAt == null, ct);
        if (server is null)
        {
            return TypedResults.NotFound();
        }

        server.Update(req.Name, req.Host, req.Port, req.Kind, req.IsDisabled, clock.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique") == true ||
                                           ex.InnerException?.Message.Contains("duplicate") == true)
        {
            return TypedResults.Conflict();
        }
        return TypedResults.Ok(ToResponse(server));
    }

    // ── soft-delete ───────────────────────────────────────────────────────────

    private static async Task<Results<NoContent, NotFound>> DeleteServer(
        ServerId id,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var server = await db.Servers
            .Include(s => s.Credentials)
            .Include(s => s.Databases)
            .SingleOrDefaultAsync(s => s.Id == id && s.DeletedAt == null, ct);
        if (server is null)
        {
            return TypedResults.NotFound();
        }

        server.SoftDelete(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static ServerResponse ToResponse(Server s) =>
        new(s.Id, s.Name, s.Kind, s.Host, s.Port, s.IsDisabled,
            s.Credentials.Select(c => new CredentialResponse(c.Id, c.Label, c.Username, c.CreatedAt, c.UpdatedAt)).ToList(),
            [
                .. s.Databases.Select(d => new DatabaseResponse(d.Id,
                    d.DisplayName,
                    d.DatabaseName,
                    d.ReadCredentialId,
                    d.WriteCredentialId,
                    d.CanWrite,
                    d.IsDisabled,
                    d.CreatedAt,
                    d.UpdatedAt)
                ).OrderBy(x => x.DisplayName)
            ],
            s.CreatedAt, s.UpdatedAt);

    // ── request / response records ────────────────────────────────────────────

    public sealed record ListServersResponse(IReadOnlyList<ServerResponse> Servers);

    public sealed record ServerResponse(
        ServerId Id,
        string Name,
        string Kind,
        string Host,
        int Port,
        bool IsDisabled,
        IReadOnlyList<CredentialResponse> Credentials, // Should credential be returned at all?
        IReadOnlyList<DatabaseResponse> Databases,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record CredentialResponse(
        CredentialId Id,
        string Label,
        string Username,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record DatabaseResponse(
        DatabaseId Id,
        string DisplayName,
        string DatabaseName,
        CredentialId ReadCredentialId,
        CredentialId? WriteCredentialId,
        bool CanWrite,
        bool IsDisabled,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record CreateServerRequest(string Name, string Kind, string Host, int Port);

    public sealed record UpdateServerRequest(
        string Name,
        string Host,
        int Port,
        string Kind,
        bool IsDisabled = false);
}