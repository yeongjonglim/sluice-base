using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
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
        // Columns come from pg_catalog rather than information_schema.columns: materialized
        // views are absent from information_schema entirely, and relkind lets us classify each
        // relation (table / view / matview) in one pass. pg_catalog is also not privilege-gated
        // for read-only roles the way information_schema is.
        const string columnsSql = """
                           SELECT n.nspname, c.relname, c.relkind,
                                  a.attname, format_type(a.atttypid, a.atttypmod) AS data_type,
                                  NOT a.attnotnull AS is_nullable
                           FROM pg_attribute a
                           JOIN pg_class c ON c.oid = a.attrelid
                           JOIN pg_namespace n ON n.oid = c.relnamespace
                           WHERE a.attnum > 0
                             AND NOT a.attisdropped
                             AND c.relkind IN ('r', 'p', 'f', 'v', 'm')
                             AND n.nspname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY n.nspname, c.relname, a.attnum;
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

        // Index columns come from indkey; expression indexes have attnum 0, so those slots
        // resolve to NULL and are rendered as "(expression)".
        const string indexesSql = """
                           SELECT n.nspname, t.relname, i.relname,
                                  ix.indisunique, ix.indisprimary, am.amname,
                                  a.attname
                           FROM pg_index ix
                           JOIN pg_class i ON i.oid = ix.indexrelid
                           JOIN pg_class t ON t.oid = ix.indrelid
                           JOIN pg_namespace n ON n.oid = t.relnamespace
                           JOIN pg_am am ON am.oid = i.relam
                           JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY AS k(attnum, ord) ON true
                           LEFT JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
                           WHERE t.relkind IN ('r', 'p', 'm')
                             AND n.nspname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY n.nspname, t.relname, i.relname, k.ord;
                           """;

        const string routinesSql = """
                           SELECT n.nspname, p.proname, p.prokind, l.lanname,
                                  pg_get_function_result(p.oid) AS return_type,
                                  pg_get_function_arguments(p.oid) AS signature,
                                  pg_get_functiondef(p.oid) AS definition
                           FROM pg_proc p
                           JOIN pg_namespace n ON n.oid = p.pronamespace
                           JOIN pg_language l ON l.oid = p.prolang
                           WHERE p.prokind IN ('f', 'p')
                             AND n.nspname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY n.nspname, p.proname;
                           """;

        // deptype 'a' (auto) links a sequence to the column that owns it, if any.
        const string sequencesSql = """
                           SELECT s.schemaname, s.sequencename, s.data_type::text,
                                  s.start_value, s.increment_by, s.min_value, s.max_value, s.cycle,
                                  ownr.owned_by
                           FROM pg_sequences s
                           LEFT JOIN LATERAL (
                               SELECT tn.nspname || '.' || tc.relname || '.' || a.attname AS owned_by
                               FROM pg_depend d
                               JOIN pg_class sc ON sc.oid = d.objid AND sc.relkind = 'S'
                               JOIN pg_namespace sn ON sn.oid = sc.relnamespace
                               JOIN pg_class tc ON tc.oid = d.refobjid
                               JOIN pg_namespace tn ON tn.oid = tc.relnamespace
                               JOIN pg_attribute a ON a.attrelid = d.refobjid AND a.attnum = d.refobjsubid
                               WHERE sn.nspname = s.schemaname AND sc.relname = s.sequencename AND d.deptype = 'a'
                               LIMIT 1
                           ) ownr ON true
                           WHERE s.schemaname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY s.schemaname, s.sequencename;
                           """;

        // typtype: e=enum, c=composite, d=domain, r=range. The relkind <> 'c' guard drops the
        // row-type composites that back every table/view, keeping only standalone CREATE TYPEs.
        const string typesSql = """
                           SELECT n.nspname, t.typname, t.typtype,
                                  CASE WHEN t.typtype = 'e' THEN
                                      (SELECT array_agg(e.enumlabel ORDER BY e.enumsortorder)
                                       FROM pg_enum e WHERE e.enumtypid = t.oid)
                                  END AS enum_labels,
                                  CASE WHEN t.typtype = 'c' THEN
                                      (SELECT array_agg(a.attname || ' ' || format_type(a.atttypid, a.atttypmod) ORDER BY a.attnum)
                                       FROM pg_attribute a
                                       WHERE a.attrelid = t.typrelid AND a.attnum > 0 AND NOT a.attisdropped)
                                  END AS attributes,
                                  CASE WHEN t.typtype = 'd' THEN format_type(t.typbasetype, t.typtypmod) END AS base_type
                           FROM pg_type t
                           JOIN pg_namespace n ON n.oid = t.typnamespace
                           WHERE t.typtype IN ('e', 'c', 'd', 'r')
                             AND NOT (t.typtype = 'c'
                                      AND EXISTS (SELECT 1 FROM pg_class c WHERE c.oid = t.typrelid AND c.relkind <> 'c'))
                             AND n.nspname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY n.nspname, t.typname;
                           """;

        const string extensionsSql = """
                           SELECT e.extname, e.extversion, n.nspname
                           FROM pg_extension e
                           JOIN pg_namespace n ON n.oid = e.extnamespace
                           ORDER BY e.extname;
                           """;

        // View and materialized-view definitions. pg_get_viewdef(oid, true) pretty-prints the
        // stored SELECT the same way psql's \d+ does; keyed back onto each relation by (schema,
        // name). relkind 'v' = view, 'm' = materialized view.
        const string relationDefinitionsSql = """
                           SELECT n.nspname, c.relname, pg_get_viewdef(c.oid, true)
                           FROM pg_class c
                           JOIN pg_namespace n ON n.oid = c.relnamespace
                           WHERE c.relkind IN ('v', 'm')
                             AND n.nspname NOT IN ('information_schema', 'pg_catalog', 'pg_toast')
                           ORDER BY n.nspname, c.relname;
                           """;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        // Columns (with relkind for classification)
        var columnRows = new List<(string Schema, string Rel, char Kind, string Column, string DataType, bool IsNullable)>();
        await using (var command = new NpgsqlCommand(columnsSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                columnRows.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetFieldValue<char>(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetBoolean(5)));
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

        // Indexes
        var indexRows = new List<(string Schema, string Rel, string Index, bool IsUnique, bool IsPrimary, string Method, string? Column)>();
        await using (var command = new NpgsqlCommand(indexesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                indexRows.Add((
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetBoolean(3), reader.GetBoolean(4), reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }

        // Routines
        var routineRows = new List<(string Schema, string Name, char Kind, string Language, string? ReturnType, string Signature, string? Definition)>();
        await using (var command = new NpgsqlCommand(routinesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                routineRows.Add((
                    reader.GetString(0), reader.GetString(1), reader.GetFieldValue<char>(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }

        // Sequences
        var sequenceRows = new List<(string Schema, string Name, string DataType, long Start, long Increment, long MinValue, long MaxValue, bool Cycle, string? OwnedBy)>();
        await using (var command = new NpgsqlCommand(sequencesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                sequenceRows.Add((
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6),
                    reader.GetBoolean(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
        }

        // Types
        var typeRows = new List<(string Schema, string Name, char TypType, string[]? EnumLabels, string[]? Attributes, string? BaseType)>();
        await using (var command = new NpgsqlCommand(typesSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                typeRows.Add((
                    reader.GetString(0), reader.GetString(1), reader.GetFieldValue<char>(2),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<string[]>(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<string[]>(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }

        // Extensions (database-level)
        var extensions = new List<ExtensionInfo>();
        await using (var command = new NpgsqlCommand(extensionsSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                extensions.Add(new ExtensionInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        // Relation definitions (views + materialized views), keyed by (schema, name).
        var definitionByRelation = new Dictionary<(string Schema, string Rel), string>();
        await using (var command = new NpgsqlCommand(relationDefinitionsSql, connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                definitionByRelation[(reader.GetString(0), reader.GetString(1))] =
                    reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
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

        var indexesByRel = indexRows
            .GroupBy(r => (r.Schema, r.Rel))
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<IndexInfo>)[.. g
                    .GroupBy(r => r.Index)
                    .Select(ig => new IndexInfo(
                        ig.Key,
                        [.. ig.Select(x => x.Column ?? "(expression)")],
                        ig.First().IsUnique,
                        ig.First().IsPrimary,
                        ig.First().Method))]);

        var routinesBySchema = routineRows
            .GroupBy(r => r.Schema)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<RoutineInfo>)[.. g.Select(r => new RoutineInfo(
                    r.Name,
                    r.Kind == 'p' ? "procedure" : "function",
                    r.ReturnType,
                    r.Language,
                    r.Signature,
                    r.Definition))]);

        var sequencesBySchema = sequenceRows
            .GroupBy(r => r.Schema)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<SequenceInfo>)[.. g.Select(r => new SequenceInfo(
                    r.Name, r.DataType, r.Start, r.Increment, r.MinValue, r.MaxValue, r.Cycle, r.OwnedBy))]);

        var typesBySchema = typeRows
            .GroupBy(r => r.Schema)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<TypeInfo>)[.. g.Select(r => new TypeInfo(
                    r.Name,
                    r.TypType switch { 'e' => "enum", 'c' => "composite", 'd' => "domain", _ => "range" },
                    r.EnumLabels,
                    r.Attributes,
                    r.BaseType))]);

        // Every schema that owns any object, whether or not it has relations with columns.
        var schemaNames = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var r in columnRows) { schemaNames.Add(r.Schema); }
        foreach (var r in routineRows) { schemaNames.Add(r.Schema); }
        foreach (var r in sequenceRows) { schemaNames.Add(r.Schema); }
        foreach (var r in typeRows) { schemaNames.Add(r.Schema); }

        var columnsBySchema = columnRows
            .GroupBy(r => r.Schema)
            .ToDictionary(g => g.Key, g => g.ToList());

        var schemas = new List<SchemaInfo>();
        foreach (var schemaName in schemaNames)
        {
            var tables = new List<TableInfo>();
            var views = new List<ViewInfo>();
            var matViews = new List<MaterializedViewInfo>();

            if (columnsBySchema.TryGetValue(schemaName, out var schemaColumns))
            {
                foreach (var rel in schemaColumns.GroupBy(r => r.Rel))
                {
                    var columns = rel.Select(c => new ColumnInfo(c.Column, c.DataType, c.IsNullable)).ToList();
                    var kind = rel.First().Kind;
                    var indexes = indexesByRel.GetValueOrDefault((schemaName, rel.Key), []);

                    switch (kind)
                    {
                        case 'v':
                            views.Add(new ViewInfo(
                                rel.Key,
                                columns,
                                definitionByRelation.GetValueOrDefault((schemaName, rel.Key))));
                            break;
                        case 'm':
                            matViews.Add(new MaterializedViewInfo(
                                rel.Key,
                                columns,
                                indexes,
                                definitionByRelation.GetValueOrDefault((schemaName, rel.Key))));
                            break;
                        default: // 'r', 'p', 'f'
                            tables.Add(new TableInfo(
                                rel.Key,
                                columns,
                                primaryKeyByTable.GetValueOrDefault((schemaName, rel.Key)),
                                foreignKeysByTable.GetValueOrDefault((schemaName, rel.Key), []),
                                indexes));
                            break;
                    }
                }
            }

            schemas.Add(new SchemaInfo(
                schemaName,
                tables,
                views,
                matViews,
                routinesBySchema.GetValueOrDefault(schemaName, []),
                sequencesBySchema.GetValueOrDefault(schemaName, []),
                typesBySchema.GetValueOrDefault(schemaName, [])));
        }

        return new SchemaTree(schemas, extensions);
    }

    public async Task<string> ExportSchemaDdlAsync(string connectionString, CancellationToken ct)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        var psi = new ProcessStartInfo
        {
            FileName = "pg_dump",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // --schema-only is hard-coded and non-overridable: this method exposes no parameter
        // that could request table data, so the data-protection invariant holds by construction.
        // --no-owner / --no-privileges keep diffs against codebase migrations clean.
        psi.ArgumentList.Add("--schema-only");
        psi.ArgumentList.Add("--no-owner");
        psi.ArgumentList.Add("--no-privileges");
        psi.ArgumentList.Add($"--host={builder.Host}");
        psi.ArgumentList.Add($"--port={builder.Port}");
        psi.ArgumentList.Add($"--username={builder.Username}");
        psi.ArgumentList.Add($"--dbname={builder.Database}");

        // Password is passed only via the child process environment — never on the command line.
        psi.Environment["PGPASSWORD"] = builder.Password ?? string.Empty;

        using var process = new Process { StartInfo = psi };
        _ = process.Start();

        // Read both pipes concurrently to avoid a full-buffer deadlock.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Don't leave pg_dump (and its Postgres connection) running after cancellation.
            process.Kill();
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"pg_dump exited with code {process.ExitCode}: {stderr}");
        }

        return stdout;
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

            // PostgreSQL interval columns are read as NpgsqlInterval rather than via GetValue:
            // Npgsql's default interval -> TimeSpan mapping throws for intervals carrying
            // non-zero months or years, since TimeSpan has no concept of months. The same
            // applies element-wise to interval[] columns.
            var dataTypeNames = Enumerable.Range(0, reader.FieldCount)
                .Select(reader.GetDataTypeName)
                .ToArray();

            while (await reader.ReadAsync(ct))
            {
                var row = new string?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.IsDBNull(i)
                        ? null
                        : dataTypeNames[i] switch
                        {
                            "interval" => FormatInterval(reader.GetFieldValue<NpgsqlInterval>(i)),
                            "interval[]" => FormatIntervalArray(reader.GetFieldValue<NpgsqlInterval?[]>(i)),
                            _ => FormatValue(reader.GetValue(i)),
                        };
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
        // System.Text.Json cannot serialize rank > 1 arrays (e.g. int[,] from a PostgreSQL
        // int[][] column), so reshape them into nested jagged arrays first.
        Array { Rank: > 1 } arr => JsonSerializer.Serialize(ToJagged(arr, []), JsonOptions),
        Array arr => JsonSerializer.Serialize(arr, arr.GetType(), JsonOptions),
        _ => value.ToString()!
    };

    // Reshapes a rectangular multi-dimensional Array into nested object?[] so it serializes
    // to nested JSON ([[1,2],[3,4]]), matching the JSON we already emit for one-dimensional
    // arrays. PostgreSQL arrays are always rectangular, so a plain recursive walk is safe.
    private static object?[] ToJagged(Array arr, int[] indices)
    {
        var dim = indices.Length;
        var length = arr.GetLength(dim);
        var result = new object?[length];
        var isLeaf = dim == arr.Rank - 1;
        for (var i = 0; i < length; i++)
        {
            int[] next = [.. indices, i];
            result[i] = isLeaf ? arr.GetValue(next) : ToJagged(arr, next);
        }

        return result;
    }

    // Renders an interval[] as a JSON array of PostgreSQL-style interval strings, keeping the
    // element-wise NpgsqlInterval read that avoids the interval -> TimeSpan crash.
    private static string FormatIntervalArray(NpgsqlInterval?[] intervals) =>
        JsonSerializer.Serialize(
            Array.ConvertAll(intervals, v => v is { } interval ? FormatInterval(interval) : null),
            JsonOptions);

    // Renders an NpgsqlInterval the way PostgreSQL prints intervals by default, e.g.
    // "1 year 2 mons 3 days 04:05:06". Months are split into years + months; the time
    // component (microseconds) becomes a signed HH:MM:SS[.ffffff] field.
    private static string FormatInterval(NpgsqlInterval interval)
    {
        var parts = new List<string>();

        var years = interval.Months / 12;
        var months = interval.Months % 12;

        if (years != 0)
        {
            parts.Add($"{years} {(Math.Abs(years) == 1 ? "year" : "years")}");
        }

        if (months != 0)
        {
            parts.Add($"{months} {(Math.Abs(months) == 1 ? "mon" : "mons")}");
        }

        if (interval.Days != 0)
        {
            parts.Add($"{interval.Days} {(Math.Abs(interval.Days) == 1 ? "day" : "days")}");
        }

        if (interval.Time != 0 || parts.Count == 0)
        {
            var negative = interval.Time < 0;
            var abs = Math.Abs(interval.Time);
            var hours = abs / 3_600_000_000L;
            var minutes = abs / 60_000_000L % 60;
            var seconds = abs / 1_000_000L % 60;
            var micros = abs % 1_000_000L;

            var sb = new StringBuilder();
            if (negative)
            {
                sb.Append('-');
            }

            sb.Append(CultureInfo.InvariantCulture, $"{hours:D2}:{minutes:D2}:{seconds:D2}");
            if (micros != 0)
            {
                sb.Append('.').Append(micros.ToString("D6", CultureInfo.InvariantCulture).TrimEnd('0'));
            }

            parts.Add(sb.ToString());
        }

        return string.Join(' ', parts);
    }

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