using SluiceBase.Api.Mcp;

namespace CoverageReport.Tests;

public class TokenHasherTests
{
    [Fact]
    public void Hash_IsStable_SameInputProducesSameOutput()
    {
        var hash1 = TokenHasher.Hash("hello");
        var hash2 = TokenHasher.Hash("hello");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Hash_DiffersPerInput()
    {
        var hash1 = TokenHasher.Hash("token-a");
        var hash2 = TokenHasher.Hash("token-b");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Generate_ProducesDistinctValues()
    {
        var a = TokenHasher.Generate();
        var b = TokenHasher.Generate();

        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData('+')]
    [InlineData('/')]
    [InlineData('=')]
    public void Generate_IsUrlSafe_DoesNotContainUnsafeChars(char unsafeChar)
    {
        // Generate enough tokens to have high confidence
        for (var i = 0; i < 50; i++)
        {
            var token = TokenHasher.Generate();
            Assert.DoesNotContain(unsafeChar, token);
        }
    }

    [Fact]
    public void ComputePkceS256Challenge_MatchesRfc7636TestVector()
    {
        // RFC 7636 Appendix B test vector
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        const string expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

        var challenge = TokenHasher.ComputePkceS256Challenge(verifier);

        Assert.Equal(expectedChallenge, challenge);
    }
}
