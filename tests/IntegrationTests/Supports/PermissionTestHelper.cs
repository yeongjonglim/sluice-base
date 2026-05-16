using System.Net.Http.Json;
using SluiceBase.Core.Permissions;

namespace IntegrationTests.Supports;

internal static class PermissionTestHelper
{
    public static async Task RevokeAllPermissionsAsync(
        AuthenticatedSession adminSession,
        string userEmail,
        string xsrf,
        CancellationToken ct)
    {
        var users = await adminSession.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var user = users!.Users.Single(u => u.Email == userEmail);

        foreach (var permission in Permissions.Global)
        {
            using var req = MutationRequest(
                HttpMethod.Delete,
                $"/api/admin/user/{user.Id}/permission/{permission}",
                xsrf);
            var response = await adminSession.Client.SendAsync(req, ct);
            response.EnsureSuccessStatusCode();
        }
    }

    public static async Task RevokePermissionAsync(
        AuthenticatedSession adminSession,
        string userEmail,
        string permission,
        string xsrf,
        CancellationToken ct)
    {
        var users = await adminSession.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var user = users!.Users.Single(u => u.Email == userEmail);

        using var req = MutationRequest(
            HttpMethod.Delete,
            $"/api/admin/user/{user.Id}/permission/{permission}",
            xsrf);
        var response = await adminSession.Client.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();
    }

    public static async Task RevokeAllDatabaseRolesAsync(
        AuthenticatedSession adminSession,
        string userEmail,
        string xsrf,
        CancellationToken ct)
    {
        var users = await adminSession.Client.GetFromJsonAsync<ListUserBody>("/api/admin/user", ct);
        var user = users!.Users.Single(u => u.Email == userEmail);

        var rolesResp = await adminSession.Client.GetFromJsonAsync<UserRolesBody>(
            $"/api/admin/user/{user.Id}/role", ct);
        if (rolesResp is null)
        {
            return;
        }

        foreach (var role in rolesResp.Roles)
        {
            using var req = MutationRequest(
                HttpMethod.Delete,
                $"/api/admin/database/{role.DatabaseId}/role/{user.Id}/{role.Permission}",
                xsrf);
            await adminSession.Client.SendAsync(req, ct);
        }
    }

    private static HttpRequestMessage MutationRequest(HttpMethod method, string url, string xsrf)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        return req;
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
    private sealed record UserRolesBody(UserRoleRow[] Roles);
    private sealed record UserRoleRow(string DatabaseId, string Permission);
}
