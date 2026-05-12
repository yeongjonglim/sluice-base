using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using Npgsql;
using SluiceBase.Core.Queries;
using SluiceBase.Core.Schemas;
using SluiceBase.Core.Targets;

namespace SluiceBase.Api.Targets;

internal sealed class PostgresTargetEngine : ITargetEngine
{
    public string Kind => "postgres";

    public async Task<ConnectivityResult> TestConnectionAsync(
        string connectionString,
        CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return new ConnectivityResult(result is 1, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectivityResult(false, ex.Message);
        }
    }

    public async Task<SchemaTree> GetSchemaAsync(string connectionString, CancellationToken ct)
    {
        const string sql = """
                           SELECT table_schema, table_name, column_name, data_type, is_nullable
                           FROM information_schema.columns
                           WHERE table_schema NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY table_schema, table_name, ordinal_position;
                           """;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var rows = new List<(string Schema, string Table, string Column, string DataType, bool IsNullable)>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4) == "YES"
            ));
        }

        var schemas = rows
            .GroupBy(r => r.Schema)
            .Select(sg => new SchemaInfo(
                sg.Key,
                [
                    .. sg.GroupBy(r => r.Table)
                        .Select(tg => new TableInfo(
                            tg.Key,
                            [.. tg.Select(c => new ColumnInfo(c.Column, c.DataType, c.IsNullable))]))
                ]))
            .ToList();

        return new SchemaTree(schemas);
    }

    public async Task<QueryData> ExecuteQueryAsync(string connectionString, string sql, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var setReadOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", conn, tx))
        {
            await setReadOnly.ExecuteNonQueryAsync(ct);
        }

        string[] columns;
        var rows = new List<string?[]>();

        await using (var cmd = new NpgsqlCommand(sql, conn, tx))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            columns = [.. Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)];

            while (await reader.ReadAsync(ct))
            {
                var row = new string?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : FormatValue(reader.GetValue(i));
                }

                rows.Add(row);
            }
        }

        await tx.CommitAsync(ct);
        return new QueryData(columns, [.. rows]);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string FormatValue(object value) => value switch
    {
        DateTime dt => dt.ToString("O"),
        DateTimeOffset dto => dto.ToUniversalTime().ToString("O"),
        DateOnly d => d.ToString("O", DateTimeFormatInfo.InvariantInfo),
        TimeOnly t => t.ToString("O", DateTimeFormatInfo.InvariantInfo),
        TimeSpan ts => XmlConvert.ToString(ts), // Format it to ISO8601
        JsonDocument doc => doc.RootElement.GetRawText(),
        JsonElement el => el.GetRawText(),
        BitArray bits => FormatBitArray(bits),
        IDictionary dict => JsonSerializer.Serialize(dict, dict.GetType(), JsonOptions),
        Array arr => JsonSerializer.Serialize(arr, arr.GetType(), JsonOptions),
        _ => value.ToString()!
    };

    private static string FormatBitArray(BitArray bits)
    {
        var sb = new StringBuilder(bits.Length);
        for (var i = 0; i < bits.Length; i++)
        {
            sb.Append(bits[i] ? '1' : '0');
        }
        return sb.ToString();
    }

    public async Task<int> ExecuteUpdateAsync(string connectionString, string sql, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        return affected;
    }
}