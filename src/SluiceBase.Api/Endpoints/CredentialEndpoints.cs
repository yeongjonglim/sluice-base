using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using SluiceBase.Api.Data;
using SluiceBase.Api.Servers;
using SluiceBase.Core.Permissions;
using SluiceBase.Core.Servers;

namespace SluiceBase.Api.Endpoints;

internal static class CredentialEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/server/{serverId}/credential")
            .RequireAuthorization(Permissions.ServerManage);

        group.MapPost("/", AddCredential).WithName("AddCredential");
        group.MapPut("/{credentialId}", UpdateCredential).WithName("UpdateCredential");
        group.MapDelete("/{credentialId}", DeleteCredential).WithName("DeleteCredential");
    }

    private static async Task<Results<Created<CredentialResponse>, NotFound>> AddCredential(
        ServerId serverId,
        AddCredentialRequest req,
        AppDbContext db,
        IDataProtectionProvider dataProtection,
        TimeProvider clock,
        CancellationToken ct)
    {
        var serverExists = await db.Servers.AnyAsync(s => s.Id == serverId && s.DeletedAt == null, ct);
        if (!serverExists)
        {
            return TypedResults.NotFound();
        }

        var protector = dataProtection.CreateProtector(ServerConnectionFactory.ProtectorPurpose);
        var cred = Credential.Create(serverId, req.Label, req.Username, protector.Protect(req.Password), clock.GetUtcNow());
        db.Credentials.Add(cred);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/server/{serverId}/credential/{cred.Id}", ToResponse(cred));
    }

    private static async Task<Results<Ok<CredentialResponse>, NotFound>> UpdateCredential(
        ServerId serverId,
        CredentialId credentialId,
        UpdateCredentialRequest req,
        AppDbContext db,
        IDataProtectionProvider dataProtection,
        TimeProvider clock,
        CancellationToken ct)
    {
        // I think credential update should not allow updating Username though...
        var cred = await db.Credentials
            .SingleOrDefaultAsync(c => c.Id == credentialId && c.ServerId == serverId && c.DeletedAt == null, ct);
        if (cred is null)
        {
            return TypedResults.NotFound();
        }

        var protector = dataProtection.CreateProtector(ServerConnectionFactory.ProtectorPurpose);
        var encPass = req.Password is not null ? protector.Protect(req.Password) : null;
        cred.Update(req.Label, req.Username, encPass, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(cred));
    }

    private static async Task<Results<NoContent, NotFound, Conflict<string>>> DeleteCredential(
        ServerId serverId,
        CredentialId credentialId,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var cred = await db.Credentials
            .SingleOrDefaultAsync(c => c.Id == credentialId && c.ServerId == serverId && c.DeletedAt == null, ct);
        if (cred is null)
        {
            return TypedResults.NotFound();
        }

        var inUse = await db.Databases.AnyAsync(
            d => d.ServerId == serverId && d.DeletedAt == null &&
                 (d.ReadCredentialId == credentialId || d.WriteCredentialId == credentialId), ct);
        if (inUse)
        {
            return TypedResults.Conflict("Credential is still referenced by one or more active databases.");
        }

        cred.SoftDelete(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static CredentialResponse ToResponse(Credential c) =>
        new(c.Id, c.Label, c.Username, c.CreatedAt, c.UpdatedAt);

    public sealed record AddCredentialRequest(string Label, string Username, string Password);
    public sealed record UpdateCredentialRequest(string Label, string Username, string? Password = null);
    public sealed record CredentialResponse(CredentialId Id, string Label, string Username, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}