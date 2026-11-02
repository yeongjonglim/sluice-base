using SluiceBase.Api.Queries;

namespace SluiceBase.Api.Tests;

public class SqlTokenizerEscapeTests
{
    private static readonly IReadOnlyList<(string Schema, string Table, string Column)> Ssn =
        [("public", "users", "ssn")];

    [Theory]
    [InlineData("SELECT id, U&\"\\0073sn\" FROM users")]
    [InlineData("SELECT id, u&\"\\+000073sn\" FROM users")]
    [InlineData("SELECT id, U&\"!0073sn\" UESCAPE '!' FROM users")]
    public void FindBlockedColumns_UnicodeEscapedIdentifier_IsDecoded(string sql)
    {
        var hit = Assert.Single(SqlColumnChecker.FindBlockedColumns(sql, Ssn));
        Assert.Equal("ssn", hit.Column);
    }

    [Fact]
    public void FindBlockedColumns_IdentifierAfterEscapeStringWithEscapedQuote_IsSeen()
    {
        var hit = Assert.Single(SqlColumnChecker.FindBlockedColumns(
            "SET application_name = E'\\''; SELECT id, ssn FROM users", Ssn));
        Assert.Equal("ssn", hit.Column);
    }

    [Fact]
    public void FindBlockedColumns_ColumnNameInsideEscapeString_IsNotAHit()
    {
        Assert.Empty(SqlColumnChecker.FindBlockedColumns("SELECT E'ssn\\' still a string' FROM users", Ssn));
    }

    [Theory]
    [InlineData("SELECT U&\"\\0071uery_to_xml\"('SELECT ssn FROM users', true, false, '')")]
    [InlineData("SELECT E'\\'', query_to_xml('SELECT ssn FROM users', true, false, '')")]
    public void DenylistFindFirst_HiddenSerializationFunction_IsFound(string sql)
    {
        Assert.Equal("query_to_xml", SerializationFunctionDenylist.FindFirst(sql));
    }
}
