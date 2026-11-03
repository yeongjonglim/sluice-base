using SluiceBase.Api.Queries;

namespace SluiceBase.Api.Tests;

public class SqlStatementSplitterTests
{
    [Fact]
    public void Split_TwoStatements_ReturnsBoth()
    {
        var parts = SqlStatementSplitter.Split("SELECT 1; SELECT 2;");
        Assert.Equal(2, parts.Count);
        Assert.Equal("SELECT 1", parts[0]);
        Assert.Equal("SELECT 2", parts[1]);
    }

    [Fact]
    public void Split_SemicolonInStringLiteral_NotASplit()
    {
        var parts = SqlStatementSplitter.Split("SELECT 'a;b' FROM t");
        Assert.Single(parts);
    }

    [Fact]
    public void Split_SemicolonInDollarQuote_NotASplit()
    {
        var parts = SqlStatementSplitter.Split("SELECT $$a;b$$ FROM t; SELECT 2");
        Assert.Equal(2, parts.Count);
    }

    [Fact]
    public void Split_SemicolonInLineComment_NotASplit()
    {
        var parts = SqlStatementSplitter.Split("SELECT 1 -- x;y\n; SELECT 2");
        Assert.Equal(2, parts.Count);
    }

    [Fact]
    public void Split_TrailingWhitespaceAndEmpties_Dropped()
    {
        var parts = SqlStatementSplitter.Split("SELECT 1;;  ;");
        Assert.Single(parts);
    }

    [Fact]
    public void Split_BackslashEscapedQuoteInEscapeString_NotAStringEnd()
    {
        var parts = SqlStatementSplitter.Split("SELECT E'\\'' AS a; COMMIT; DELETE FROM t");
        Assert.Equal(["SELECT E'\\'' AS a", "COMMIT", "DELETE FROM t"], parts);
    }

    [Fact]
    public void Split_QuoteAfterIdentifierEndingInE_IsAPlainString()
    {
        // Only a lone E opens an escape string; in name'...' the backslash is an ordinary character.
        var parts = SqlStatementSplitter.Split("SELECT name'\\'; SELECT 2");
        Assert.Equal(2, parts.Count);
    }
}
