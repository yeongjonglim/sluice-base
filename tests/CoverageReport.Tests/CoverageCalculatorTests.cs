namespace CoverageReport.Tests;

public class CoverageCalculatorTests
{
    [Fact]
    public void ComputeChangeCoverage_MatchingFile_ReturnsRate()
    {
        var files = new List<FileCoverage>
        {
            new("src/Api/Query.cs", 80.0, [14]),
            new("src/Api/Update.cs", 50.0, [21, 22]),
        };

        var result = CoverageCalculator.ComputeChangeCoverage(files, ["src/Api/Query.cs"]);

        Assert.Equal(80.0, result.ChangeRate);
        Assert.Single(result.MatchedFiles);
    }

    [Fact]
    public void ComputeChangeCoverage_NoMatch_ReturnsNull()
    {
        var files = new List<FileCoverage>
        {
            new("src/Api/Query.cs", 80.0, [14]),
        };

        var result = CoverageCalculator.ComputeChangeCoverage(files, ["src/Api/NotAFile.cs"]);

        Assert.Null(result.ChangeRate);
        Assert.Empty(result.MatchedFiles);
    }

    [Fact]
    public void ComputeChangeCoverage_MultipleMatches_ReturnsAverage()
    {
        var files = new List<FileCoverage>
        {
            new("src/Api/Query.cs", 80.0, [14]),
            new("src/Api/Update.cs", 50.0, [21, 22]),
        };

        var result = CoverageCalculator.ComputeChangeCoverage(
            files, ["src/Api/Query.cs", "src/Api/Update.cs"]);

        Assert.Equal(65.0, result.ChangeRate);
        Assert.Equal(2, result.MatchedFiles.Count);
    }

    [Theory]
    [InlineData(new int[0], "")]
    [InlineData(new[] { 5 }, "5")]
    [InlineData(new[] { 1, 2, 3 }, "1-3")]
    [InlineData(new[] { 1, 2, 3, 5, 7, 8, 9 }, "1-3, 5, 7-9")]
    [InlineData(new[] { 9, 1, 3, 2, 7, 8, 5 }, "1-3, 5, 7-9")]
    [InlineData(new[] { 1, 1, 2, 2, 3 }, "1-3")]
    public void CollapseLineRanges_ReturnsExpected(int[] lines, string expected)
    {
        Assert.Equal(expected, CoverageCalculator.CollapseLineRanges(lines.ToList()));
    }

    [Theory]
    [InlineData("./src/foo.cs", "src/foo.cs")]
    [InlineData("src/foo.cs", "src/foo.cs")]
    [InlineData("src\\foo.cs", "src/foo.cs")]
    public void NormalizePath_ReturnsExpected(string input, string expected)
    {
        Assert.Equal(expected, CoverageCalculator.NormalizePath(input));
    }
}
