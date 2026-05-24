using SluiceBase.Api.Queries;

namespace IntegrationTests;

public class SqlTokenizerTests
{
    [Fact]
    public void Tokenize_ExtractsIdentifiers()
    {
        var result = SqlTokenizer.Tokenize("SELECT email, name FROM users");
        Assert.Contains("email", result.Identifiers);
        Assert.Contains("name", result.Identifiers);
        Assert.Contains("users", result.Identifiers);
    }

    [Fact]
    public void Tokenize_SkipsSingleQuotedStringContent()
    {
        var result = SqlTokenizer.Tokenize("SELECT id FROM users WHERE note = 'email address'");
        Assert.DoesNotContain("email", result.Identifiers);
        Assert.DoesNotContain("address", result.Identifiers);
    }

    [Fact]
    public void Tokenize_SkipsLineComments()
    {
        var result = SqlTokenizer.Tokenize("SELECT id FROM users -- email column");
        Assert.DoesNotContain("email", result.Identifiers);
    }

    [Fact]
    public void Tokenize_SkipsBlockComments()
    {
        var result = SqlTokenizer.Tokenize("SELECT /* email */ id FROM users");
        Assert.DoesNotContain("email", result.Identifiers);
    }

    [Fact]
    public void Tokenize_SkipsNestedBlockComments()
    {
        var result = SqlTokenizer.Tokenize("SELECT /* /* email */ inner */ id FROM users");
        Assert.DoesNotContain("email", result.Identifiers);
        Assert.DoesNotContain("inner", result.Identifiers);
    }

    [Fact]
    public void Tokenize_SkipsDollarQuotedStrings()
    {
        var result = SqlTokenizer.Tokenize("SELECT $$email$$, id FROM users");
        Assert.DoesNotContain("email", result.Identifiers);
    }

    [Fact]
    public void Tokenize_SkipsTaggedDollarQuotedStrings()
    {
        var result = SqlTokenizer.Tokenize("SELECT $body$email$body$, id FROM users");
        Assert.DoesNotContain("email", result.Identifiers);
    }

    [Fact]
    public void Tokenize_ExtractsDoubleQuotedIdentifiers()
    {
        var result = SqlTokenizer.Tokenize("SELECT \"email\" FROM users");
        Assert.Contains("email", result.Identifiers);
    }

    [Fact]
    public void Tokenize_SkipsPrefixStringContent()
    {
        var result = SqlTokenizer.Tokenize("SELECT id FROM users WHERE note = E'email'");
        Assert.DoesNotContain("email", result.Identifiers);
    }

    [Fact]
    public void Tokenize_PriceAndPriceTypeAreDistinctTokens()
    {
        var result = SqlTokenizer.Tokenize("SELECT price_type FROM orders");
        Assert.Contains("price_type", result.Identifiers);
        Assert.DoesNotContain("price", result.Identifiers);
    }

    [Fact]
    public void Tokenize_DetectsWildcard()
    {
        var result = SqlTokenizer.Tokenize("SELECT * FROM users");
        Assert.True(result.HasWildcard);
    }

    [Fact]
    public void Tokenize_WildcardInsideString_NotDetected()
    {
        var result = SqlTokenizer.Tokenize("SELECT id FROM users WHERE note LIKE '%*%'");
        Assert.False(result.HasWildcard);
    }

    [Fact]
    public void Tokenize_UppercaseKeywords_ExtractedAsIdentifiers()
    {
        var result = SqlTokenizer.Tokenize("SELECT EMAIL, FIRST_NAME FROM USERS");
        Assert.Contains("EMAIL", result.Identifiers);
        Assert.Contains("FIRST_NAME", result.Identifiers);
        Assert.Contains("USERS", result.Identifiers);
    }

    [Fact]
    public void Tokenize_MixedCasing_ExtractedVerbatim()
    {
        var result = SqlTokenizer.Tokenize("SELECT Email FROM Users");
        Assert.Contains("Email", result.Identifiers);
        Assert.DoesNotContain("email", result.Identifiers);
        Assert.DoesNotContain("EMAIL", result.Identifiers);
    }

    [Fact]
    public void Tokenize_IdentifiersWithNumbers_Extracted()
    {
        var result = SqlTokenizer.Tokenize("SELECT order_v2, column1, v3_price FROM orders_2024");
        Assert.Contains("order_v2", result.Identifiers);
        Assert.Contains("column1", result.Identifiers);
        Assert.Contains("v3_price", result.Identifiers);
        Assert.Contains("orders_2024", result.Identifiers);
    }

    [Fact]
    public void Tokenize_MultilineQuery_ExtractsAllIdentifiers()
    {
        var sql = """
            SELECT
              id,
              email,
              created_at
            FROM users
            WHERE status = 'active'
            ORDER BY created_at DESC
            """;
        var result = SqlTokenizer.Tokenize(sql);
        Assert.Contains("id", result.Identifiers);
        Assert.Contains("email", result.Identifiers);
        Assert.Contains("created_at", result.Identifiers);
        Assert.Contains("users", result.Identifiers);
        Assert.Contains("status", result.Identifiers);
    }

    [Fact]
    public void Tokenize_CTE_ExtractsIdentifiersFromBothParts()
    {
        var sql = """
            WITH active_users AS (
                SELECT id, email FROM users WHERE active = true
            )
            SELECT id, email FROM active_users
            """;
        var result = SqlTokenizer.Tokenize(sql);
        Assert.Contains("active_users", result.Identifiers);
        Assert.Contains("id", result.Identifiers);
        Assert.Contains("email", result.Identifiers);
        Assert.Contains("users", result.Identifiers);
        Assert.Contains("active", result.Identifiers);
    }

    [Fact]
    public void Tokenize_SchemaQualifiedColumn_ExtractsSeparateTokens()
    {
        var result = SqlTokenizer.Tokenize("SELECT public.users.email FROM public.users");
        Assert.Contains("public", result.Identifiers);
        Assert.Contains("users", result.Identifiers);
        Assert.Contains("email", result.Identifiers);
        Assert.DoesNotContain("public.users", result.Identifiers);
        Assert.DoesNotContain("users.email", result.Identifiers);
    }
}
