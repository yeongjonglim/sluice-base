using SluiceBase.Api.Queries;

namespace SluiceBase.Api.Tests;

public class SerializationFunctionDenylistTests
{
    [Theory]
    [InlineData("SELECT query_to_xml('SELECT ssn FROM users', true, false, '')", "query_to_xml")]
    [InlineData("SELECT table_to_xml('users', true, false, '')", "table_to_xml")]
    [InlineData("SELECT SCHEMA_TO_XML('public', true, false, '')", "schema_to_xml")]
    [InlineData("SELECT database_to_xml(true, false, '')", "database_to_xml")]
    public void FindFirst_DenylistedFunction_ReturnsName(string sql, string expected)
    {
        Assert.Equal(expected, SerializationFunctionDenylist.FindFirst(sql));
    }

    [Fact]
    public void FindFirst_PlainSelect_ReturnsNull()
    {
        Assert.Null(SerializationFunctionDenylist.FindFirst("SELECT email FROM users"));
    }

    [Fact]
    public void FindFirst_NameOnlyInsideStringLiteral_ReturnsNull()
    {
        // The tokenizer skips string contents, so a name mentioned in a literal is not a call.
        Assert.Null(SerializationFunctionDenylist.FindFirst("SELECT 'query_to_xml is scary' FROM users"));
    }
}
