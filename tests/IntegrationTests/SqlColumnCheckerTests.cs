using SluiceBase.Api.Queries;

namespace IntegrationTests;

public class SqlColumnCheckerTests
{
    [Fact]
    public void FindBlockedColumns_SimpleSelect_ReturnsHit()
    {
        var blocked = new[] { ("public", "users", "email") };
        var hits = SqlColumnChecker.FindBlockedColumns("SELECT email FROM users", blocked);
        Assert.Single(hits);
        Assert.Equal("email", hits[0].Column);
    }

    [Fact]
    public void FindBlockedColumns_SafeColumn_ReturnsEmpty()
    {
        var blocked = new[] { ("public", "users", "email") };
        var hits = SqlColumnChecker.FindBlockedColumns("SELECT name FROM users", blocked);
        Assert.Empty(hits);
    }

    [Fact]
    public void FindBlockedColumns_WhereClause_Detected()
    {
        var blocked = new[] { ("public", "users", "email") };
        var hits = SqlColumnChecker.FindBlockedColumns(
            "SELECT id FROM users WHERE email = 'x@example.com'", blocked);
        Assert.Single(hits);
    }

    [Fact]
    public void FindBlockedColumns_ColumnInStringLiteral_NotBlocked()
    {
        var blocked = new[] { ("public", "users", "email") };
        var hits = SqlColumnChecker.FindBlockedColumns(
            "SELECT id FROM users WHERE note = 'email address'", blocked);
        Assert.Empty(hits);
    }

    [Fact]
    public void FindBlockedColumns_ColumnInComment_NotBlocked()
    {
        var blocked = new[] { ("public", "users", "email") };
        var hits = SqlColumnChecker.FindBlockedColumns(
            "SELECT id FROM users -- email is sensitive", blocked);
        Assert.Empty(hits);
    }

    [Fact]
    public void FindBlockedColumns_Wildcard_BlocksAllSensitiveColumns()
    {
        var blocked = new[] { ("public", "users", "email"), ("public", "users", "ssn") };
        var hits = SqlColumnChecker.FindBlockedColumns("SELECT * FROM users", blocked);
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void FindBlockedColumns_PriceVsPriceType_OnlyExactNameBlocked()
    {
        var blocked = new[] { ("public", "orders", "price") };
        var hits = SqlColumnChecker.FindBlockedColumns("SELECT price_type FROM orders", blocked);
        Assert.Empty(hits);
    }

    [Fact]
    public void FindBlockedColumns_NoSensitiveColumns_ReturnsEmpty()
    {
        var hits = SqlColumnChecker.FindBlockedColumns("SELECT email FROM users", []);
        Assert.Empty(hits);
    }

    [Fact]
    public void FindBlockedColumns_UppercaseColumn_MatchesCaseInsensitively()
    {
        var blocked = new[] { ("public", "users", "email") };
        var hits = SqlColumnChecker.FindBlockedColumns("SELECT EMAIL FROM users", blocked);
        Assert.Single(hits);
    }

    [Fact]
    public void FindBlockedColumns_SchemaQualified_MatchesColumnName()
    {
        var blocked = new[] { ("public", "users", "email") };
        var hits = SqlColumnChecker.FindBlockedColumns(
            "SELECT public.users.email FROM public.users", blocked);
        Assert.Single(hits);
    }

    [Fact]
    public void FindBlockedColumns_CTE_DetectsColumnInCteBody()
    {
        var blocked = new[] { ("public", "users", "email") };
        var sql = """
            WITH cte AS (SELECT id, email FROM users WHERE active = true)
            SELECT id FROM cte
            """;
        var hits = SqlColumnChecker.FindBlockedColumns(sql, blocked);
        Assert.Single(hits);
    }

    [Fact]
    public void FindBlockedColumns_ToJsonb_BlocksSensitiveColumns()
    {
        var blocked = new[] { ("public", "employees", "ssn") };
        var hits = SqlColumnChecker.FindBlockedColumns(
            "SELECT to_jsonb(e) FROM public.employees e", blocked);
        Assert.Single(hits);
        Assert.Equal("ssn", hits[0].Column);
    }

    [Fact]
    public void FindBlockedColumns_RowToJson_BlocksSensitiveColumns()
    {
        var blocked = new[] { ("public", "users", "email") };
        var hits = SqlColumnChecker.FindBlockedColumns(
            "SELECT row_to_json(users) FROM users", blocked);
        Assert.Single(hits);
        Assert.Equal("email", hits[0].Column);
    }

    [Fact]
    public void FindBlockedColumns_ToJson_BlocksSensitiveColumns()
    {
        var blocked = new[] { ("public", "users", "email"), ("public", "users", "ssn") };
        var hits = SqlColumnChecker.FindBlockedColumns(
            "SELECT to_json(u) FROM users u", blocked);
        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void FindBlockedColumns_JsonAgg_BlocksSensitiveColumns()
    {
        var blocked = new[] { ("public", "users", "email") };
        var hits = SqlColumnChecker.FindBlockedColumns(
            "SELECT jsonb_agg(users) FROM users", blocked);
        Assert.Single(hits);
    }

    [Fact]
    public void FindBlockedColumns_RowSerializationOnUnrelatedTable_NoBlock()
    {
        var blocked = new[] { ("public", "users", "email") };
        var hits = SqlColumnChecker.FindBlockedColumns(
            "SELECT to_jsonb(o) FROM orders o", blocked);
        Assert.Empty(hits);
    }

    [Fact]
    public void FindBlockedColumns_RowSerializationCaseInsensitive_Blocks()
    {
        var blocked = new[] { ("public", "users", "email") };
        var hits = SqlColumnChecker.FindBlockedColumns(
            "SELECT TO_JSONB(u) FROM Users u", blocked);
        Assert.Single(hits);
    }

    // Known false positive: using a row-serialization function on a scalar value
    // (not a row) still triggers a block when the query references a table with
    // sensitive columns. This is acceptable — over-blocking is preferred over
    // allowing potential data exfiltration.
    [Fact]
    public void FindBlockedColumns_ToJsonbOnScalar_FalsePositiveBlocks()
    {
        var blocked = new[] { ("public", "users", "email") };
        var hits = SqlColumnChecker.FindBlockedColumns(
            "SELECT to_jsonb(name) FROM users", blocked);
        Assert.Single(hits);
    }
}
