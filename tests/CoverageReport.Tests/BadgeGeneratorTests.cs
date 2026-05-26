namespace CoverageReport.Tests;

public class BadgeGeneratorTests
{
    [Theory]
    [InlineData(70.0, "#4c1")]
    [InlineData(95.0, "#4c1")]
    [InlineData(50.0, "#dfb317")]
    [InlineData(65.0, "#dfb317")]
    [InlineData(30.0, "#e05d44")]
    [InlineData(49.9, "#e05d44")]
    public void GetColor_ReturnsCorrectColor(double rate, string expected)
    {
        Assert.Equal(expected, BadgeGenerator.GetColor(rate));
    }

    [Fact]
    public void Generate_ProducesValidSvg()
    {
        var svg = BadgeGenerator.Generate("coverage", 75.0);

        Assert.StartsWith("<svg", svg.TrimStart());
        Assert.Contains("75.0%", svg);
        Assert.Contains("#4c1", svg);
    }

    [Fact]
    public void Generate_RedBadge()
    {
        var svg = BadgeGenerator.Generate("coverage", 30.0);

        Assert.Contains("#e05d44", svg);
    }

    [Fact]
    public void Generate_YellowBadge()
    {
        var svg = BadgeGenerator.Generate("coverage", 55.0);

        Assert.Contains("#dfb317", svg);
    }
}
