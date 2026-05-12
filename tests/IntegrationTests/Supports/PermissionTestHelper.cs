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

        foreach (var permission in Permissions.All)
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

    private static HttpRequestMessage MutationRequest(HttpMethod method, string url, string xsrf)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        return req;
    }

    private sealed record ListUserBody(UserRow[] Users);
    private sealed record UserRow(string Id, string Email);
}
