using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
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
        const string columnsSql = """
                           SELECT table_schema, table_name, column_name, data_type, is_nullable
                           FROM information_schema.columns
                           WHERE table_schema NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY table_schema, table_name, ordinal_position;
                           """;

        const string primaryKeysSql = """
                           SELECT tc.table_schema, tc.table_name, kcu.column_name
                           FROM information_schema.table_constraints tc
                           JOIN information_schema.key_column_usage kcu
                             ON tc.constraint_name = kcu.constraint_name
                            AND tc.table_schema = kcu.table_schema
                           WHERE tc.constraint_type = 'PRIMARY KEY'
                             AND tc.table_schema NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY tc.table_schema, tc.table_name, kcu.ordinal_position;
                           """;

        // The referenced side is joined via a second key_column_usage (ccu), not
        // constraint_column_usage, and aligned on position_in_unique_constraint. This pairs
        // each FK column with its referenced column by ordinal position so composite foreign
        // keys map correctly; constraint_column_usage would produce a cartesian cross-join.
        const string foreignKeysSql = """
                           SELECT
                               rc.constraint_name,
                               kcu.table_schema, kcu.table_name, kcu.column_name,
                               ccu.table_schema AS ref_schema,
                               ccu.table_name   AS ref_table,
                               ccu.column_name  AS ref_column
                           FROM information_schema.referential_constraints rc
                           JOIN information_schema.key_column_usage kcu
                             ON kcu.constraint_name = rc.constraint_name
                            AND kcu.constraint_schema = rc.constraint_schema
                           JOIN information_schema.key_column_usage ccu
                             ON ccu.constraint_name = rc.unique_constraint_name
                            AND ccu.constraint_schema = rc.unique_constraint_schema
                            AND ccu.ordinal_position = kcu.position_in_unique_constraint
                           WHERE kcu.table_schema NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY rc.constraint_name, kcu.ordinal_position;
                           """;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        // Columns
        var columnRows = new List<(string Schema, string Table, string Column, string DataType, bool IsNullable)>();
        await using (var command = new NpgsqlCommand(columnsSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                columnRows.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4) == "YES"));
            }
        }

        // Primary keys
        var pkRows = new List<(string Schema, string Table, string Column)>();
        await using (var command = new NpgsqlCommand(primaryKeysSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                pkRows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        // Foreign keys
        var fkRows = new List<(string Constraint, string Schema, string Table, string Column, string RefSchema, string RefTable, string RefColumn)>();
        await using (var command = new NpgsqlCommand(foreignKeysSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                fkRows.Add((
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6)));
            }
        }

        var schemas = columnRows
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

        var primaryKeys = pkRows
            .GroupBy(r => (r.Schema, r.Table))
            .Select(g => new PrimaryKey(g.Key.Schema, g.Key.Table, [.. g.Select(r => r.Column)]))
            .ToList();

        var foreignKeys = fkRows
            .GroupBy(r => r.Constraint)
            .Select(g => new ForeignKey(
                g.Key,
                g.First().Schema,
                g.First().Table,
                [.. g.Select(r => r.Column)],
                g.First().RefSchema,
                g.First().RefTable,
                [.. g.Select(r => r.RefColumn)]))
            .ToList();

        return new SchemaTree(schemas, primaryKeys, foreignKeys);
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
        TimeSpan ts => ts.ToString("c"),
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