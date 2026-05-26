namespace CoverageReport.Tests;

public class MarkdownRendererTests
{
    [Fact]
    public void Render_PassingThresholds_ReturnsNotFailed()
    {
        var result = MarkdownRenderer.Render(
            label: "Backend",
            overallRate: 75.0,
            overallThreshold: 70.0,
            changeRate: 85.0,
            changeThreshold: 80.0,
            changedFilesCoverage: [new("src/Query.cs", 85.0, [14])],
            allFiles: [new("src/Query.cs", 85.0, [14]), new("src/Update.cs", 50.0, [21, 22])],
            lowestCount: 5);

        Assert.False(result.Failed);
        Assert.Contains("<!-- coverage-Backend -->", result.Markdown);
        Assert.Contains("75.0%", result.Markdown);
        Assert.Contains("✅", result.Markdown);
    }

    [Fact]
    public void Render_FailingOverall_ReturnsFailed()
    {
        var result = MarkdownRenderer.Render(
            label: "Frontend",
            overallRate: 60.0,
            overallThreshold: 70.0,
            changeRate: 85.0,
            changeThreshold: 80.0,
            changedFilesCoverage: [],
            allFiles: [],
            lowestCount: 5);

        Assert.True(result.Failed);
        Assert.Contains("❌", result.Markdown);
    }

    [Fact]
    public void Render_FailingChange_ReturnsFailed()
    {
        var result = MarkdownRenderer.Render(
            label: "Frontend",
            overallRate: 75.0,
            overallThreshold: 70.0,
            changeRate: 70.0,
            changeThreshold: 80.0,
            changedFilesCoverage: [],
            allFiles: [],
            lowestCount: 5);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Render_NoChangedFiles_ShowsNA()
    {
        var result = MarkdownRenderer.Render(
            label: "Backend",
            overallRate: 75.0,
            overallThreshold: 70.0,
            changeRate: null,
            changeThreshold: 80.0,
            changedFilesCoverage: [],
            allFiles: [],
            lowestCount: 5);

        Assert.False(result.Failed);
        Assert.Contains("N/A", result.Markdown);
    }
}
