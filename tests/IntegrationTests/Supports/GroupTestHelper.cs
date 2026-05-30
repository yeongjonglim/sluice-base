using System.Net.Http.Json;

namespace IntegrationTests.Supports;

internal static class GroupTestHelper
{
    public static async Task<string> CreateGroupAsync(
        AuthenticatedSession adminSession,
        string name,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/admin/group");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        req.Content = JsonContent.Create(new { name, description = $"Test group: {name}" });
        var resp = await adminSession.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<GroupBody>(ct);
        return body!.Id;
    }

    public static async Task DeleteGroupAsync(
        AuthenticatedSession adminSession,
        string groupId,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/group/{groupId}");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        await adminSession.Client.SendAsync(req, ct);
    }

    public static async Task AddMemberAsync(
        AuthenticatedSession adminSession,
        string groupId,
        string userId,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/group/{groupId}/member");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        req.Content = JsonContent.Create(new { userId });
        var resp = await adminSession.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    public static async Task GrantGroupPermissionAsync(
        AuthenticatedSession adminSession,
        string groupId,
        string permission,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/group/{groupId}/permission");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        req.Content = JsonContent.Create(new { permission });
        var resp = await adminSession.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    private sealed record GroupBody(string Id, string Name);
}
