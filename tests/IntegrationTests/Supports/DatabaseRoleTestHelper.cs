using System.Net.Http.Json;

namespace IntegrationTests.Supports;

internal static class DatabaseRoleTestHelper
{
    public static async Task AssignByDatabaseAsync(
        AuthenticatedSession adminSession,
        string userId,
        string permission,
        string databaseId,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/role");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        req.Content = JsonContent.Create(new { userId, permission });
        var resp = await adminSession.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    public static async Task RemoveRoleAsync(
        AuthenticatedSession adminSession,
        string databaseId,
        string userId,
        string permission,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Delete, $"/api/admin/database/{databaseId}/role/{userId}/{permission}");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        var resp = await adminSession.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

}
