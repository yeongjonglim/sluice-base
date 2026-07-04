using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Endpoints;

internal static class DatabaseEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/server/{serverId}/database")
            .RequireAuthorization(Permissions.ServerManage);

        group.MapPost("/", AddDatabase).WithName("AddDatabase");
        group.MapPut("/{databaseId}", UpdateDatabase).WithName("UpdateDatabase");
        group.MapDelete("/{databaseId}", DeleteDatabase).WithName("DeleteDatabase");
        group.MapPost("/{databaseId}/test", TestDatabaseConnection).WithName("TestDatabaseConnection");
    }

    private static async Task<Results<Created<DatabaseResponse>, NotFound>> AddDatabase(
        ServerId serverId,
        AddDatabaseRequest req,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var serverExists = await db.Servers.AnyAsync(s => s.Id == serverId && s.DeletedAt == null, ct);
        if (!serverExists)
        {
            return TypedResults.NotFound();
        }

        var dbRecord = Database.Create(serverId,
            req.DisplayName,
            req.DatabaseName,
            req.ReadCredentialId,
            req.WriteCredentialId,
            clock.GetUtcNow());
        db.Databases.Add(dbRecord);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/server/{serverId}/database/{dbRecord.Id}", ToResponse(dbRecord));
    }

    private static async Task<Results<Ok<DatabaseResponse>, NotFound>> UpdateDatabase(
        ServerId serverId,
        DatabaseId databaseId,
        UpdateDatabaseRequest req,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var dbRecord = await db.Databases
            .SingleOrDefaultAsync(d => d.Id == databaseId && d.ServerId == serverId && d.DeletedAt == null, ct);
        if (dbRecord is null)
        {
            return TypedResults.NotFound();
        }

        dbRecord.Update(req.DisplayName, req.DatabaseName, req.ReadCredentialId, req.WriteCredentialId, req.IsDisabled, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(dbRecord));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteDatabase(
        ServerId serverId,
        DatabaseId databaseId,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var dbRecord = await db.Databases
            .SingleOrDefaultAsync(d => d.Id == databaseId && d.ServerId == serverId && d.DeletedAt == null, ct);
        if (dbRecord is null)
        {
            return TypedResults.NotFound();
        }

        dbRecord.SoftDelete(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<TestConnectionResponse>, NotFound>> TestDatabaseConnection(
        ServerId serverId,
        DatabaseId databaseId,
        AppDbContext db,
        IServerConnectionFactory factory,
        ITargetEngineRegistry engineRegistry,
        CancellationToken ct)
    {
        var dbRecord = await db.Databases.AsNoTracking()
            .Include(d => d.Server)
            .SingleOrDefaultAsync(d => d.Id == databaseId && d.ServerId == serverId && d.DeletedAt == null, ct);
        if (dbRecord is null)
        {
            return TypedResults.NotFound();
        }

        var targetEngine = engineRegistry.Resolve(dbRecord.Server!.Kind);

        var readCs = await factory.GetConnectionStringAsync(databaseId, CredentialKind.Read, ct);
        var readResult = await targetEngine.TestConnectionAsync(readCs, ct);

        ConnectivityResult? writeResult = null;
        if (dbRecord.CanWrite)
        {
            var writeCs = await factory.GetConnectionStringAsync(databaseId, CredentialKind.Write, ct);
            writeResult = await targetEngine.TestConnectionAsync(writeCs, ct);
        }

        return TypedResults.Ok(new TestConnectionResponse(readResult, writeResult));
    }

    private static DatabaseResponse ToResponse(Database d) =>
        new(d.Id, d.DisplayName, d.DatabaseName, d.ReadCredentialId, d.WriteCredentialId, d.CanWrite, d.IsDisabled, d.CreatedAt, d.UpdatedAt);

    public sealed record AddDatabaseRequest(
        string DisplayName,
        string DatabaseName,
        CredentialId ReadCredentialId,
        CredentialId? WriteCredentialId = null);

    public sealed record UpdateDatabaseRequest(
        string DisplayName,
        string DatabaseName,
        CredentialId ReadCredentialId,
        CredentialId? WriteCredentialId,
        bool IsDisabled);

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

    public sealed record TestConnectionResponse(
        ConnectivityResult Read,
        ConnectivityResult? Write);
}