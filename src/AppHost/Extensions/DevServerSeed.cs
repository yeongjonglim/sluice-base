using System.Net.Http.Json;
using Npgsql;

namespace AppHost.Extensions;

internal static class DevServerSeed
{
    public static async Task<ExecuteCommandResult> SeedAsync(
        ExecuteCommandContext context,
        IResourceBuilder<ProjectResource> api,
        IResourceBuilder<PostgresDatabaseResource> metadataDb,
        IResourceBuilder<PostgresDatabaseResource> blueDb,
        IResourceBuilder<PostgresDatabaseResource> greenDb)
    {
        var ct = context.CancellationToken;
        try
        {
            var apiUrl = await api.Resource.GetEndpoint("https").GetValueAsync(ct)
                         ?? throw new InvalidOperationException("API endpoint not resolved.");

            var metaConnStr = await metadataDb.Resource.ConnectionStringExpression.GetValueAsync(ct)
                              ?? throw new InvalidOperationException("Metadata connection string not resolved.");
            var blueConnStr = await blueDb.Resource.ConnectionStringExpression.GetValueAsync(ct)
                              ?? throw new InvalidOperationException("blue-appdb connection string not resolved.");
            var greenConnStr = await greenDb.Resource.ConnectionStringExpression.GetValueAsync(ct)
                               ?? throw new InvalidOperationException("green-appdb connection string not resolved.");

            var bluePg = new NpgsqlConnectionStringBuilder(blueConnStr);
            var greenPg = new NpgsqlConnectionStringBuilder(greenConnStr);

            // Encrypt passwords via the dev-only endpoint
            var blueReadEnc = await EncryptAsync(apiUrl, "reader_blue", ct);
            var blueWriteEnc = await EncryptAsync(apiUrl, "writer_blue", ct);
            var greenReadEnc = await EncryptAsync(apiUrl, "reader_green", ct);

            // Insert server records directly into metadata DB
            await using var conn = new NpgsqlConnection(metaConnStr);
            await conn.OpenAsync(ct);

            await UpsertServerAsync(conn,
                name: "Blue",
                kind: "postgres",
                host: bluePg.Host!,
                port: bluePg.Port,
                database: "appdb",
                readUser: "reader_blue",
                encReadPass: blueReadEnc,
                writeUser: "writer_blue",
                encWritePass: blueWriteEnc,
                ct);

            await UpsertServerAsync(conn,
                name: "Green",
                kind: "postgres",
                host: greenPg.Host!,
                port: greenPg.Port,
                database: "appdb",
                readUser: "reader_green",
                encReadPass: greenReadEnc,
                writeUser: null,
                encWritePass: null,
                ct);

            return CommandResults.Success();
        }
        catch (Exception ex)
        {
            return CommandResults.Failure(ex.Message);
        }
    }

    private static async Task<string> EncryptAsync(string apiBaseUrl, string plaintext, CancellationToken ct)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(apiBaseUrl) };
        var resp = await http.PostAsJsonAsync("/api/internal/dev/encrypt", new { plaintext }, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<EncryptResponse>(ct);
        return body!.Ciphertext;
    }

    private static async Task UpsertServerAsync(NpgsqlConnection conn,
        string name,
        string kind,
        string host,
        int port,
        string database,
        string readUser,
        string encReadPass,
        string? writeUser,
        string? encWritePass,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO server (
                              id, name, kind, host, port, database,
                              read_username, encrypted_read_password,
                              write_username, encrypted_write_password,
                              is_enabled, created_at, updated_at)
                          VALUES (
                              gen_random_uuid(), @name, @kind, @host, @port, @database,
                              @readUser, @encReadPass,
                              @writeUser, @encWritePass,
                              true, now(), now())
                          ON CONFLICT (name) DO NOTHING;
                          """;
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("kind", kind);
        cmd.Parameters.AddWithValue("host", host);
        cmd.Parameters.AddWithValue("port", port);
        cmd.Parameters.AddWithValue("database", database);
        cmd.Parameters.AddWithValue("readUser", readUser);
        cmd.Parameters.AddWithValue("encReadPass", encReadPass);
        cmd.Parameters.AddWithValue("writeUser", (object?)writeUser ?? DBNull.Value);
        cmd.Parameters.AddWithValue("encWritePass", (object?)encWritePass ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private sealed record EncryptResponse(string Ciphertext);
}