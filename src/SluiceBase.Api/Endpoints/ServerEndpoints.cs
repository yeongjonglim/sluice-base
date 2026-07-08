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

    private static async Task<Results<Created<ServerResponse>, Conflict, BadRequest<string>>> CreateServer(
        CreateServerRequest req,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        Server? server = req.Kind switch
        {
            "postgres" => PostgresServer.Create(req.Name, req.Host, req.Port, now),
            "mongodb" => MongoServer.Create(req.Name, req.Host, req.Port, now,
                req.ConnectionMode, req.AuthSource, req.ReplicaSet, req.UseTls),
            _ => null,
        };
        if (server is null)
        {
            return TypedResults.BadRequest($"Unsupported server kind '{req.Kind}'.");
        }

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

    private static async Task<Results<Ok<ServerResponse>, NotFound, Conflict, BadRequest<string>>> UpdateServer(
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

        // Kind is immutable — a server cannot change engine after creation.
        if (!string.Equals(req.Kind, server.Kind, StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.BadRequest("A server's kind cannot be changed.");
        }

        server.UpdateCore(req.Name, req.Host, req.Port, req.IsDisabled, clock.GetUtcNow());
        if (server is MongoServer mongo)
        {
            mongo.UpdateMongo(req.ConnectionMode, req.AuthSource, req.ReplicaSet, req.UseTls);
        }

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

    private static ServerResponse ToResponse(Server s)
    {
        var mongo = s as MongoServer;
        return new(s.Id, s.Name, s.Kind, s.Host, s.Port,
            mongo?.ConnectionMode, mongo?.AuthSource, mongo?.ReplicaSet, mongo?.UseTls,
            s.IsDisabled,
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
    }

    // ── request / response records ────────────────────────────────────────────

    public sealed record ListServersResponse(IReadOnlyList<ServerResponse> Servers);

    public sealed record ServerResponse(
        ServerId Id,
        string Name,
        string Kind,
        string Host,
        int Port,
        ConnectionMode? ConnectionMode,
        string? AuthSource,
        string? ReplicaSet,
        bool? UseTls,
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

    public sealed record CreateServerRequest(
        string Name,
        string Kind,
        string Host,
        int Port,
        ConnectionMode ConnectionMode = ConnectionMode.Standard,
        string? AuthSource = null,
        string? ReplicaSet = null,
        bool UseTls = false);

    public sealed record UpdateServerRequest(
        string Name,
        string Host,
        int Port,
        string Kind,
        bool IsDisabled = false,
        ConnectionMode ConnectionMode = ConnectionMode.Standard,
        string? AuthSource = null,
        string? ReplicaSet = null,
        bool UseTls = false);
}