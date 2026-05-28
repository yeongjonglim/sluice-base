using System.Globalization;
using CoverageReport;

var command = args.Length > 0 ? args[0] : "report";

if (command == "report")
{
    RunReport(args[1..]);
}
else if (command == "badge")
{
    RunBadge(args[1..]);
}
else
{
    Console.Error.WriteLine($"Unknown command: {command}. Use 'report' or 'badge'.");
    Environment.Exit(1);
}

static void RunReport(string[] args)
{
    string? coberturaPath = null;
    string? label = null;
    var overallThreshold = 70.0;
    var changeThreshold = 80.0;
    string? prNumber = null;
    string? sourcePrefix = null;
    var lowestCount = 5;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--cobertura-path": coberturaPath = args[++i]; break;
            case "--label": label = args[++i]; break;
            case "--overall-threshold": overallThreshold = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
            case "--change-threshold": changeThreshold = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
            case "--pr-number": prNumber = args[++i]; break;
            case "--lowest-files-count": lowestCount = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
            case "--source-prefix": sourcePrefix = args[++i]; break;
        }
    }

    if (coberturaPath is null || label is null || prNumber is null)
    {
        Console.Error.WriteLine("Required: --cobertura-path, --label, --pr-number");
        Environment.Exit(1);
    }

    var data = CoverageParser.Parse(coberturaPath);
    var changedFiles = GitHubCommentPoster.GetChangedFiles(prNumber);
    var changeCoverage = CoverageCalculator.ComputeChangeCoverage(data.Files, changedFiles, sourcePrefix);

    var result = MarkdownRenderer.Render(
        label: label,
        overallRate: data.OverallLineRate,
        overallThreshold: overallThreshold,
        changeRate: changeCoverage.ChangeRate,
        changeThreshold: changeThreshold,
        changedFilesCoverage: changeCoverage.MatchedFiles,
        allFiles: data.Files,
        lowestCount: lowestCount);

    GitHubCommentPoster.PostOrUpdateComment(prNumber, label, result.Markdown);

    if (result.Failed)
    {
        Console.Error.WriteLine($"FAIL: Coverage thresholds not met for {label}");
        Console.Error.WriteLine($"  Overall: {data.OverallLineRate:F1}% (threshold: {overallThreshold:F0}%)");
        if (changeCoverage.ChangeRate is not null)
        {
            Console.Error.WriteLine($"  Changed: {changeCoverage.ChangeRate:F1}% (threshold: {changeThreshold:F0}%)");
        }

        Environment.Exit(1);
    }

    Console.WriteLine($"PASS: Coverage thresholds met for {label}");
    Console.WriteLine($"  Overall: {data.OverallLineRate:F1}% (threshold: {overallThreshold:F0}%)");
    if (changeCoverage.ChangeRate is not null)
    {
        Console.WriteLine($"  Changed: {changeCoverage.ChangeRate:F1}% (threshold: {changeThreshold:F0}%)");
    }
}

static void RunBadge(string[] args)
{
    string? coberturaPath = null;
    string? label = null;
    string? output = null;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--cobertura-path": coberturaPath = args[++i]; break;
            case "--label": label = args[++i]; break;
            case "--output": output = args[++i]; break;
        }
    }

    if (coberturaPath is null || label is null || output is null)
    {
        Console.Error.WriteLine("Required: --cobertura-path, --label, --output");
        Environment.Exit(1);
    }

    var data = CoverageParser.Parse(coberturaPath);
    var svg = BadgeGenerator.Generate(label, data.OverallLineRate);

    var dir = Path.GetDirectoryName(output);
    if (!string.IsNullOrEmpty(dir))
    {
        Directory.CreateDirectory(dir);
    }

    File.WriteAllText(output, svg);
    Console.WriteLine($"Badge generated: {label} = {data.OverallLineRate:F1}% → {output}");
}
