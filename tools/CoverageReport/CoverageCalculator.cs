namespace CoverageReport;

public record ChangeCoverageResult(double? ChangeRate, List<FileCoverage> MatchedFiles);

public static class CoverageCalculator
{
    public static ChangeCoverageResult ComputeChangeCoverage(
        List<FileCoverage> fileCoverage,
        List<string> changedFiles,
        string? sourcePrefix = null)
    {
        var normalizedPrefix = sourcePrefix is not null
            ? NormalizePath(sourcePrefix).TrimEnd('/') + "/"
            : null;

        var changedSet = changedFiles
            .Select(f =>
            {
                var normalized = NormalizePath(f);
                if (normalizedPrefix is not null &&
                    normalized.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized[normalizedPrefix.Length..];
                }
                return normalized;
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matched = fileCoverage
            .Where(f =>
            {
                var normalized = NormalizePath(f.Filename);
                return changedSet.Any(changed =>
                    string.Equals(normalized, changed, StringComparison.OrdinalIgnoreCase) ||
                    normalized.EndsWith("/" + changed, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();

        if (matched.Count == 0)
        {
            return new ChangeCoverageResult(null, []);
        }

        var averageRate = matched.Average(f => f.LineRate);
        return new ChangeCoverageResult(averageRate, matched);
    }

    public static string NormalizePath(string path)
    {
        return path
            .Replace('\\', '/')
            .TrimStart('.', '/');
    }

    public static string CollapseLineRanges(List<int> lines)
    {
        if (lines.Count == 0)
        {
            return "";
        }

        var sorted = lines.Distinct().OrderBy(n => n).ToList();
        var ranges = new List<string>();
        var start = sorted[0];
        var prev = sorted[0];

        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] == prev + 1)
            {
                prev = sorted[i];
            }
            else
            {
                ranges.Add(start == prev ? $"{start}" : $"{start}-{prev}");
                start = sorted[i];
                prev = sorted[i];
            }
        }

        ranges.Add(start == prev ? $"{start}" : $"{start}-{prev}");
        return string.Join(", ", ranges);
    }
}
