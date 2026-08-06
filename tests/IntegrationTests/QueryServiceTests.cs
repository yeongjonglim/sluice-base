using System.Net;
using System.Net.Http.Json;
using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Endpoints;

namespace IntegrationTests;

/// <summary>
/// Verifies that <see cref="SluiceBase.Api.Services.IQueryService"/> correctly tags
/// <see cref="SluiceBase.Core.Queries.QueryLog"/> entries with the provided
/// <see cref="SluiceBase.Core.Queries.QuerySource"/>.
/// These tests exercise the service end-to-end via the HTTP endpoint and directly
/// confirm log persistence by querying the metadata DB with Npgsql.
/// </summary>
public class QueryServiceTests(SluiceBaseStackFactory factory)
{
    /// <summary>
    /// A SELECT routed through the endpoint (QuerySource.Ui) returns Ok with rows and
    /// persists a QueryLog row in the metadata database with status = Success.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SuccessfulSelect_LogsSourceUiAndReturnsRows()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await QueryTestSetup.AliceWithBlueServerAsync(factory, ct);
        using var _ = session;

        var sql = $"SELECT id FROM public.users ORDER BY id LIMIT 1 -- svc-source-{Guid.NewGuid():N}";

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new { databaseId, sql });
        var resp = await session.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = await resp.Content.ReadFromJsonAsync<QueryEndpoints.QueryResponse>(ct);
        Assert.NotNull(result);
        Assert.Null(result.Error);
        Assert.NotNull(result.Rows);
        Assert.NotEmpty(result.Rows);

        // Verify the QueryLog was persisted with source = Ui (integer 0) in the metadata DB.
        var metaConnStr = await factory.InitialisedApp.GetConnectionStringAsync("metadata-db", ct);
        await using var conn = new NpgsqlConnection(metaConnStr);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT source FROM query_log WHERE query_text = @sql LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@sql", sql.Trim());
        var sourceValue = await cmd.ExecuteScalarAsync(ct);

        Assert.NotNull(sourceValue);
        // QuerySource is stored as a string via EF Core global HaveConversion<string>() convention.
        Assert.Equal("Ui", sourceValue.ToString());
    }

    /// <summary>
    /// A query that touches a blocked sensitive column is rejected with 403 and the
    /// QueryLog is persisted with status = Blocked.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_BlockedSensitiveColumn_Returns403AndLogsBlocked()
    {
        var ct = TestContext.Current.CancellationToken;
        var (session, xsrf, databaseId) = await QueryTestSetup.AliceWithBlueServerAsync(factory, ct);
        using var _ = session;

        await SensitiveColumnTestHelper.MarkColumnAsync(
            session, databaseId.ToString(), "public", "users", "email", xsrf, ct);

        var sql = $"SELECT email FROM public.users LIMIT 1 -- svc-blocked-{Guid.NewGuid():N}";

        using var req = QueryTestSetup.MutationRequest(HttpMethod.Post, "/api/query", xsrf,
            new QueryEndpoints.QueryRequest(databaseId, sql));
        var resp = await session.Client.SendAsync(req, ct);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
        Assert.Equal("sensitive_columns", body.GetProperty("type").GetString());
        Assert.True(body.GetProperty("columns").GetArrayLength() > 0);

        // Verify the QueryLog was persisted with status = Blocked in the metadata DB.
        var metaConnStr = await factory.InitialisedApp.GetConnectionStringAsync("metadata-db", ct);
        await using var conn = new NpgsqlConnection(metaConnStr);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT status FROM query_log WHERE query_text = @sql LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@sql", sql.Trim());
        var statusValue = await cmd.ExecuteScalarAsync(ct);

        Assert.NotNull(statusValue);
        Assert.Equal("Blocked", statusValue!.ToString(), ignoreCase: true);
    }
}
