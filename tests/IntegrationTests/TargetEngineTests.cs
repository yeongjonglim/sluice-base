using Aspire.Hosting.Testing;
using IntegrationTests.Supports;
using Npgsql;
using SluiceBase.Api.Targets;

namespace IntegrationTests;

public sealed class TargetEngineTests(SluiceBaseStackFactory factory)
{
    private readonly PostgresTargetEngine _targetEngine = new();

    [Fact]
    public async Task TargetEngine_Postgres_TestConnection_Succeeds()
    {
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", TestContext.Current.CancellationToken);

        Assert.NotNull(connectionString);

        var result = await _targetEngine.TestConnectionAsync(
            connectionString,
            TestContext.Current.CancellationToken);

        Assert.True(result.Ok, result.Error);
        Assert.Null(result.Error);
        Assert.Equal("postgres", _targetEngine.Kind);
    }

    [Fact]
    public async Task TargetEngine_Postgres_TestConnection_Fails_OnBadConnString()
    {
        const string brokenConnectionString =
            "Host=does-not-exist.invalid;Port=65000;Database=appdb;Username=u;Password=p;Timeout=2";

        var result = await _targetEngine.TestConnectionAsync(
            brokenConnectionString,
            TestContext.Current.CancellationToken);

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task TargetEngine_Postgres_GetSchema_ReturnsPublicSchemaForBlue()
    {
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", TestContext.Current.CancellationToken);

        Assert.NotNull(connectionString);

        var tree = await _targetEngine.GetSchemaAsync(
            connectionString,
            TestContext.Current.CancellationToken);

        Assert.NotNull(tree);
        Assert.DoesNotContain(tree.Schemas, s => s.Name == "information_schema");
        Assert.DoesNotContain(tree.Schemas, s => s.Name == "pg_catalog");
        var publicSchema = Assert.Single(tree.Schemas, s => s.Name == "public");
        Assert.Contains(publicSchema.Tables, t => t.Name == "users");
        var usersTable = publicSchema.Tables.Single(t => t.Name == "users");
        Assert.NotEmpty(usersTable.Columns);
        Assert.All(usersTable.Columns, c => Assert.NotEmpty(c.DataType));
    }

    [Fact]
    public async Task TargetEngine_Postgres_ExecuteQuery_ReturnsData()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        var result = await _targetEngine.ExecuteQueryAsync(
            connectionString,
            "SELECT id, email FROM public.users ORDER BY id LIMIT 5",
            ct);

        Assert.NotNull(result);
        Assert.Equal(2, result.Columns.Length);
        Assert.Equal("id", result.Columns[0]);
        Assert.Equal("email", result.Columns[1]);
        Assert.NotEmpty(result.Rows);
        Assert.All(result.Rows, row => Assert.Equal(2, row.Length));
    }

    [Fact]
    public async Task TargetEngine_Postgres_ExecuteQuery_ReadsIntervalWithMonths()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        // Intervals with non-zero months/years cannot be read as TimeSpan; the engine must
        // read them as NpgsqlInterval instead of crashing.
        var result = await _targetEngine.ExecuteQueryAsync(
            connectionString,
            "SELECT interval '1 year 2 months 3 days 04:05:06' AS span",
            ct);

        Assert.NotNull(result);
        Assert.Single(result.Columns);
        Assert.Equal("span", result.Columns[0]);
        var value = Assert.Single(result.Rows)[0];
        Assert.Equal("1 year 2 mons 3 days 04:05:06", value);
    }

    [Fact]
    public async Task TargetEngine_Postgres_ExecuteQuery_ReadsIntervalArrayWithMonths()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        // interval[] hits the same interval -> TimeSpan crash element-wise; it is read as
        // NpgsqlInterval[] and rendered as a JSON array of PostgreSQL-style interval strings.
        var result = await _targetEngine.ExecuteQueryAsync(
            connectionString,
            "SELECT ARRAY[interval '1 year 2 months', interval '3 days 04:05:06'] AS spans",
            ct);

        Assert.NotNull(result);
        var value = Assert.Single(result.Rows)[0];
        Assert.Equal("[\"1 year 2 mons\",\"3 days 04:05:06\"]", value);
    }

    [Fact]
    public async Task TargetEngine_Postgres_ExecuteQuery_ReadsMultiDimensionalArray()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        // System.Text.Json cannot serialize rank > 1 arrays; the engine reshapes them into
        // nested JSON, matching how one-dimensional arrays already render.
        var result = await _targetEngine.ExecuteQueryAsync(
            connectionString,
            "SELECT ARRAY[[1,2],[3,4]] AS grid",
            ct);

        Assert.NotNull(result);
        var value = Assert.Single(result.Rows)[0];
        Assert.Equal("[[1,2],[3,4]]", value);
    }

    [Fact]
    public async Task TargetEngine_Postgres_ExecuteQuery_ReturnsEmptyRows_ForNoResults()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        var result = await _targetEngine.ExecuteQueryAsync(
            connectionString,
            "SELECT id FROM public.users WHERE 1 = 0",
            ct);

        Assert.NotNull(result);
        Assert.Single(result.Columns);
        Assert.Empty(result.Rows);
    }

    [Theory]
    [InlineData("INSERT INTO public.users (id, email) VALUES (9999, 'injected@test.com')")]
    [InlineData("UPDATE public.users SET email = 'x@x.com'")]
    [InlineData("DELETE FROM public.users")]
    public async Task TargetEngine_Postgres_ExecuteQuery_RejectsMutatingStatements(string sql)
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
            await _targetEngine.ExecuteQueryAsync(connectionString, sql, ct));

        // PostgreSQL error code 25006 = read_only_sql_transaction
        Assert.Equal("25006", ex.SqlState);
    }

    [Fact]
    public async Task TargetEngine_Postgres_ExecuteUpdate_AffectsRows()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        // Use write credentials from the blue DB seed (writer_blue/writer_blue)
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Username = "writer_blue",
            Password = "writer_blue",
        };

        var result = await _targetEngine.ExecuteUpdateAsync(
            builder.ConnectionString,
            "UPDATE public.users SET email = email WHERE 1=0",
            ct);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task TargetEngine_Postgres_ExecuteUpdate_ThrowsOnInvalidSql()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", ct);
        Assert.NotNull(connectionString);

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Username = "writer_blue",
            Password = "writer_blue",
        };

        await Assert.ThrowsAsync<PostgresException>(async () =>
            await _targetEngine.ExecuteUpdateAsync(
                builder.ConnectionString,
                "UPDATE public.nonexistent SET foo = bar",
                ct));
    }

    [Fact]
    public async Task TargetEngine_Postgres_ExportSchemaDdl_ReturnsSchemaOnlyDdl()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await factory.InitialisedApp
            .GetConnectionStringAsync("blue-appdb", ct);

        Assert.NotNull(connectionString);

        var ddl = await _targetEngine.ExportSchemaDdlAsync(connectionString, ct);

        // Structure is present...
        Assert.Contains("CREATE TABLE", ddl);
        Assert.Contains("users", ddl);
        // ...but no data is ever emitted (data-protection invariant).
        Assert.DoesNotContain("COPY ", ddl);
        Assert.DoesNotContain("INSERT INTO", ddl);
    }
}
