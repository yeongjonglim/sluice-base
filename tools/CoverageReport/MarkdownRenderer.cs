using System.Globalization;
using System.Text;

namespace CoverageReport;

public record RenderResult(string Markdown, bool Failed);

public static class MarkdownRenderer
{
    public static RenderResult Render(
        string label,
        double overallRate,
        double overallThreshold,
        double? changeRate,
        double changeThreshold,
        List<FileCoverage> changedFilesCoverage,
        List<FileCoverage> allFiles,
        int lowestCount)
    {
        var marker = $"<!-- coverage-{label} -->";
        var overallPass = overallRate >= overallThreshold;
        var changePass = changeRate is null || changeRate >= changeThreshold;
        var failed = !overallPass || !changePass;

        var sb = new StringBuilder();
        sb.AppendLine(marker);
        sb.AppendLine(CultureInfo.InvariantCulture, $"## Coverage Report — {label}");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value | Threshold | Status |");
        sb.AppendLine("|--------|-------|-----------|--------|");
        sb.AppendLine(CultureInfo.InvariantCulture, $"| Overall line coverage | {overallRate:F1}% | {overallThreshold:F0}% | {(overallPass ? "✅" : "❌")} |");

        if (changeRate is not null)
        {
            var icon = changeRate >= changeThreshold ? "✅" : "❌";
            sb.AppendLine(CultureInfo.InvariantCulture, $"| Changed lines covered | {changeRate:F1}% | {changeThreshold:F0}% | {icon} |");
        }
        else
        {
            sb.AppendLine("| Changed lines covered | N/A (no instrumented files changed) | — | — |");
        }

        if (changedFilesCoverage.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Changed file coverage");
            sb.AppendLine();
            sb.AppendLine("| File | Coverage | Uncovered lines |");
            sb.AppendLine("|------|----------|-----------------|");

            foreach (var file in changedFilesCoverage.OrderBy(f => f.LineRate))
            {
                var uncov = file.UncoveredLines.Count > 0
                    ? CoverageCalculator.CollapseLineRanges(file.UncoveredLines)
                    : "—";
                sb.AppendLine(CultureInfo.InvariantCulture, $"| `{file.Filename}` | {file.LineRate:F1}% | {uncov} |");
            }
        }

        var lowest = allFiles.OrderBy(f => f.LineRate).Take(lowestCount).ToList();
        if (lowest.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Lowest coverage files (project-wide)");
            sb.AppendLine();
            sb.AppendLine("| File | Coverage |");
            sb.AppendLine("|------|----------|");

            foreach (var file in lowest)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"| `{file.Filename}` | {file.LineRate:F1}% |");
            }
        }

        return new RenderResult(sb.ToString(), failed);
    }
}
