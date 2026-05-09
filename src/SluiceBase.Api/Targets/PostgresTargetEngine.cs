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
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i).ToString();
                }

                rows.Add(row);
            }
        }

        await tx.CommitAsync(ct);
        return new QueryData(columns, [.. rows]);
    }
}