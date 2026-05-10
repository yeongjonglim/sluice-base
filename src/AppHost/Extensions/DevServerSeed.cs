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

            var blueReadEnc = await EncryptAsync(apiUrl, "reader_blue", ct);
            var blueWriteEnc = await EncryptAsync(apiUrl, "writer_blue", ct);
            var greenReadEnc = await EncryptAsync(apiUrl, "reader_green", ct);

            await using var conn = new NpgsqlConnection(metaConnStr);
            await conn.OpenAsync(ct);

            await SeedServerAsync(conn,
                serverName: "Blue",
                kind: "postgres",
                host: bluePg.Host!,
                port: bluePg.Port,
                readLabel: "Read-only role",
                readUser: "reader_blue",
                encReadPass: blueReadEnc,
                writeLabel: "Write role",
                writeUser: "writer_blue",
                encWritePass: blueWriteEnc,
                dbDisplayName: "Blue App DB",
                dbName: "appdb",
                ct);

            await SeedServerAsync(conn,
                serverName: "Green",
                kind: "postgres",
                host: greenPg.Host!,
                port: greenPg.Port,
                readLabel: "Read-only role",
                readUser: "reader_green",
                encReadPass: greenReadEnc,
                writeLabel: null,
                writeUser: null,
                encWritePass: null,
                dbDisplayName: "Green App DB",
                dbName: "appdb",
                ct);

            return CommandResults.Success();
        }
        catch (Exception ex)
        {
            return CommandResults.Failure(ex.Message);
        }
    }

    private static async Task SeedServerAsync(
        NpgsqlConnection conn,
        string serverName,
        string kind,
        string host,
        int port,
        string readLabel,
        string readUser,
        string encReadPass,
        string? writeLabel,
        string? writeUser,
        string? encWritePass,
        string dbDisplayName,
        string dbName,
        CancellationToken ct)
    {
        // Insert server (no-op if already seeded)
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO server (id, name, kind, host, port, is_disabled, created_at, updated_at)
                VALUES (gen_random_uuid(), @name, @kind, @host, @port, false, now(), now())
                ON CONFLICT (name) WHERE deleted_at IS NULL DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("name", serverName);
            cmd.Parameters.AddWithValue("kind", kind);
            cmd.Parameters.AddWithValue("host", host);
            cmd.Parameters.AddWithValue("port", port);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        Guid serverId;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM server WHERE name = @name AND deleted_at IS NULL;";
            cmd.Parameters.AddWithValue("name", serverName);
            serverId = (Guid)(await cmd.ExecuteScalarAsync(ct))!;
        }

        // Insert read credential
        var readCredId = await UpsertCredentialAsync(conn, serverId, readLabel, readUser, encReadPass, ct);

        // Insert write credential (optional)
        Guid? writeCredId = null;
        if (writeLabel is not null && writeUser is not null && encWritePass is not null)
        {
            writeCredId = await UpsertCredentialAsync(conn, serverId, writeLabel, writeUser, encWritePass, ct);
        }

        // Insert database
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO server_database (
                    id, server_id, display_name, database_name,
                    read_credential_id, write_credential_id,
                    is_disabled, created_at, updated_at)
                VALUES (
                    gen_random_uuid(), @serverId, @displayName, @dbName,
                    @readCredId, @writeCredId,
                    false, now(), now())
                ON CONFLICT DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("serverId", serverId);
            cmd.Parameters.AddWithValue("displayName", dbDisplayName);
            cmd.Parameters.AddWithValue("dbName", dbName);
            cmd.Parameters.AddWithValue("readCredId", readCredId);
            cmd.Parameters.AddWithValue("writeCredId", (object?)writeCredId ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<Guid> UpsertCredentialAsync(
        NpgsqlConnection conn,
        Guid serverId,
        string label,
        string username,
        string encryptedPassword,
        CancellationToken ct)
    {
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO server_credential (id, server_id, label, username, encrypted_password, created_at, updated_at)
                VALUES (gen_random_uuid(), @serverId, @label, @username, @encPass, now(), now())
                ON CONFLICT DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("serverId", serverId);
            cmd.Parameters.AddWithValue("label", label);
            cmd.Parameters.AddWithValue("username", username);
            cmd.Parameters.AddWithValue("encPass", encryptedPassword);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await using var selectCmd = conn.CreateCommand();
        selectCmd.CommandText = "SELECT id FROM server_credential WHERE server_id = @serverId AND username = @username AND deleted_at IS NULL LIMIT 1;";
        selectCmd.Parameters.AddWithValue("serverId", serverId);
        selectCmd.Parameters.AddWithValue("username", username);
        return (Guid)(await selectCmd.ExecuteScalarAsync(ct))!;
    }

    private static async Task<string> EncryptAsync(string apiBaseUrl, string plaintext, CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
        var resp = await http.PostAsJsonAsync("/api/internal/dev/encrypt", new { plaintext }, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<EncryptResponse>(ct);
        return body!.Ciphertext;
    }

    private sealed record EncryptResponse(string Ciphertext);
}