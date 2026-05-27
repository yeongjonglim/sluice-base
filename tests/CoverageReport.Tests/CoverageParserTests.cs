namespace CoverageReport.Tests;

public class CoverageParserTests
{
    private readonly string _fixturePath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "sample-cobertura.xml");

    [Fact]
    public void Parse_ReturnsCorrectOverallRate()
    {
        var result = CoverageParser.Parse(_fixturePath);

        Assert.Equal(75.0, result.OverallLineRate, precision: 1);
    }

    [Fact]
    public void Parse_ReturnsCorrectFileCount()
    {
        var result = CoverageParser.Parse(_fixturePath);

        Assert.Equal(3, result.Files.Count);
    }

    [Fact]
    public void Parse_ReturnsCorrectFileRates()
    {
        var result = CoverageParser.Parse(_fixturePath);

        var query = result.Files.Single(f => f.Filename.Contains("QueryEndpoint"));
        Assert.Equal(80.0, query.LineRate, precision: 1);
        Assert.Contains(14, query.UncoveredLines);

        var update = result.Files.Single(f => f.Filename.Contains("UpdateEndpoint"));
        Assert.Equal(50.0, update.LineRate, precision: 1);
        Assert.Contains(21, update.UncoveredLines);
        Assert.Contains(22, update.UncoveredLines);

        var health = result.Files.Single(f => f.Filename.Contains("HealthEndpoint"));
        Assert.Equal(100.0, health.LineRate, precision: 1);
        Assert.Empty(health.UncoveredLines);
    }
}
