using System.Net.Http.Json;

namespace IntegrationTests.Supports;

internal static class SensitiveColumnTestHelper
{
    public static async Task<string> MarkColumnAsync(
        AuthenticatedSession adminSession,
        string databaseId,
        string schemaName,
        string tableName,
        string columnName,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"/api/admin/database/{databaseId}/sensitive-column");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        req.Content = JsonContent.Create(new { schemaName, tableName, columnName });
        var resp = await adminSession.Client.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var list = await adminSession.Client.GetFromJsonAsync<SensitiveColumnListBody>(
            $"/api/admin/database/{databaseId}/sensitive-column", ct);
        return list!.Columns
            .Single(c => c.SchemaName == schemaName && c.TableName == tableName && c.ColumnName == columnName)
            .Id;
    }

    public static async Task GrantBypassAsync(
        AuthenticatedSession adminSession,
        string databaseId,
        string sensitiveColumnId,
        string userId,
        string xsrf,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/admin/database/{databaseId}/sensitive-column/{sensitiveColumnId}/bypass");
        req.Headers.Add("X-XSRF-TOKEN", xsrf);
        req.Content = JsonContent.Create(new { userId });
        (await adminSession.Client.SendAsync(req, ct)).EnsureSuccessStatusCode();
    }

    public sealed record SensitiveColumnListBody(SensitiveColumnRow[] Columns);
    public sealed record SensitiveColumnRow(string Id, string SchemaName, string TableName, string ColumnName);
}
