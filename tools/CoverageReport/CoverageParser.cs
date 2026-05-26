using Palmmedia.ReportGenerator.Core.Parser;
using Palmmedia.ReportGenerator.Core.Parser.Analysis;
using Palmmedia.ReportGenerator.Core.Parser.Filtering;

namespace CoverageReport;

public record FileCoverage(string Filename, double LineRate, List<int> UncoveredLines);

public record CoverageData(double OverallLineRate, List<FileCoverage> Files);

public static class CoverageParser
{
    public static CoverageData Parse(string path)
    {
        var filter = new DefaultFilter([]);
        var parser = new CoverageReportParser(1, 1, [], filter, filter, filter);
        var result = parser.ParseFiles([path]);

        var totalCoverable = 0;
        var totalCovered = 0;
        var fileCoverages = new Dictionary<string, (int Coverable, int Covered, List<int> Uncovered)>();

        foreach (var assembly in result.Assemblies)
        {
            foreach (var cls in assembly.Classes)
            {
                foreach (var file in cls.Files)
                {
                    totalCoverable += file.CoverableLines;
                    totalCovered += file.CoveredLines;

                    var uncoveredLines = new List<int>();
                    for (var line = 1; line <= (file.TotalLines ?? 0); line++)
                    {
                        if (file.LineVisitStatus[line] == LineVisitStatus.NotCovered)
                        {
                            uncoveredLines.Add(line);
                        }
                    }

                    if (fileCoverages.TryGetValue(file.Path, out var existing))
                    {
                        fileCoverages[file.Path] = (
                            existing.Coverable + file.CoverableLines,
                            existing.Covered + file.CoveredLines,
                            existing.Uncovered.Union(uncoveredLines).Distinct().ToList()
                        );
                    }
                    else
                    {
                        fileCoverages[file.Path] = (file.CoverableLines, file.CoveredLines, uncoveredLines);
                    }
                }
            }
        }

        var files = fileCoverages.Select(kvp =>
        {
            var rate = kvp.Value.Coverable > 0
                ? (double)kvp.Value.Covered / kvp.Value.Coverable * 100
                : 100.0;
            return new FileCoverage(kvp.Key, rate, kvp.Value.Uncovered);
        }).ToList();

        var overallRate = totalCoverable > 0
            ? (double)totalCovered / totalCoverable * 100
            : 100.0;

        return new CoverageData(overallRate, files);
    }
}
