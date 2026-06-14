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

        // Constraints are read from pg_catalog, not information_schema: information_schema's
        // table_constraints / referential_constraints views only expose constraints on tables
        // where the current role has a privilege OTHER THAN SELECT, so the read-only credential
        // used for introspection sees none of them. pg_catalog is not privilege-gated this way.
        const string primaryKeysSql = """
                           SELECT n.nspname, c.relname, a.attname
                           FROM pg_constraint con
                           JOIN pg_class c ON c.oid = con.conrelid
                           JOIN pg_namespace n ON n.oid = c.relnamespace
                           JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY (con.conkey)
                           WHERE con.contype = 'p'
                             AND n.nspname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY n.nspname, c.relname, array_position(con.conkey, a.attnum);
                           """;

        // unnest(conkey, confkey) WITH ORDINALITY pairs each FK column with its referenced
        // column by position, so composite foreign keys map correctly.
        const string foreignKeysSql = """
                           SELECT
                               con.conname,
                               n.nspname, c.relname, att.attname,
                               rn.nspname AS ref_schema,
                               rc.relname AS ref_table,
                               ratt.attname AS ref_column
                           FROM pg_constraint con
                           JOIN pg_class c ON c.oid = con.conrelid
                           JOIN pg_namespace n ON n.oid = c.relnamespace
                           JOIN pg_class rc ON rc.oid = con.confrelid
                           JOIN pg_namespace rn ON rn.oid = rc.relnamespace
                           JOIN LATERAL unnest(con.conkey, con.confkey) WITH ORDINALITY AS k(conkey, confkey, ord) ON true
                           JOIN pg_attribute att ON att.attrelid = con.conrelid AND att.attnum = k.conkey
                           JOIN pg_attribute ratt ON ratt.attrelid = con.confrelid AND ratt.attnum = k.confkey
                           WHERE con.contype = 'f'
                             AND n.nspname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY con.conname, k.ord;
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

        var primaryKeyByTable = pkRows
            .GroupBy(r => (r.Schema, r.Table))
            .ToDictionary(g => g.Key, g => new PrimaryKey([.. g.Select(r => r.Column)]));

        var foreignKeysByTable = fkRows
            .GroupBy(r => r.Constraint)
            .Select(g => (
                Owner: (g.First().Schema, g.First().Table),
                ForeignKey: new ForeignKey(
                    g.Key,
                    [.. g.Select(r => r.Column)],
                    g.First().RefSchema,
                    g.First().RefTable,
                    [.. g.Select(r => r.RefColumn)])))
            .GroupBy(x => x.Owner)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ForeignKey>)[.. g.Select(x => x.ForeignKey)]);

        var schemas = columnRows
            .GroupBy(r => r.Schema)
            .Select(sg => new SchemaInfo(
                sg.Key,
                [
                    .. sg.GroupBy(r => r.Table)
                        .Select(tg => new TableInfo(
                            tg.Key,
                            [.. tg.Select(c => new ColumnInfo(c.Column, c.DataType, c.IsNullable))],
                            primaryKeyByTable.GetValueOrDefault((sg.Key, tg.Key)),
                            foreignKeysByTable.GetValueOrDefault((sg.Key, tg.Key), [])))
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