# Test Coverage and Effectiveness Measurement — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add coverage collection, PR reporting with threshold enforcement, and coverage badges — all self-contained in GitHub Actions with no external dependencies.

**Architecture:** Backend coverage via coverlet (already collecting), frontend coverage via `@vitest/coverage-v8` (new). A C#/.NET console tool parses Cobertura XML and posts PR comments with overall/per-change/per-file metrics, failing the job if thresholds aren't met. A separate badge workflow runs on main merges and commits SVG badges to `gh-pages`.

**Tech Stack:** C# / .NET 10 (System.Xml.Linq), Vitest + @vitest/coverage-v8, coverlet (XPlat Code Coverage), GitHub Actions, gh CLI

---

### Task 1: Add frontend coverage collection

**Files:**
- Modify: `src/frontend/vitest.config.ts`
- Modify: `src/frontend/package.json` (via npm)

- [ ] **Step 1: Install `@vitest/coverage-v8`**

Run from `src/frontend/`:

```bash
npm install --save-dev @vitest/coverage-v8
```

- [ ] **Step 2: Add coverage config to `vitest.config.ts`**

Replace the full file contents with:

```typescript
import { URL, fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";
import viteReact from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [viteReact()],
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test-setup.ts"],
    globals: false,
    include: ["src/**/*.test.{ts,tsx}"],
    coverage: {
      provider: "v8",
      reporter: ["cobertura", "text"],
      reportsDirectory: "./coverage",
      thresholds: {
        lines: 70,
        branches: 70,
        functions: 70,
        statements: 70,
      },
    },
  },
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
});
```

- [ ] **Step 3: Update test script in `package.json`**

Change the `test` script from `"vitest run"` to `"vitest run --coverage"`.

- [ ] **Step 4: Add `coverage/` to `.gitignore`**

Check if `src/frontend/.gitignore` exists. If so, append `coverage/` to it. If not, create it with:

```
coverage/
```

- [ ] **Step 5: Run tests locally to verify coverage works**

Run from `src/frontend/`:

```bash
npm run test
```

Expected: Tests pass, a `coverage/` directory is created containing `cobertura-coverage.xml`, and a text summary is printed to the terminal showing line/branch/function/statement percentages.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/vitest.config.ts src/frontend/package.json src/frontend/package-lock.json src/frontend/.gitignore
git commit -m "feat: add vitest coverage collection with v8 provider"
```

---

### Task 2: Create the CoverageReport tool project

**Files:**
- Create: `tools/CoverageReport/CoverageReport.csproj`
- Create: `tools/CoverageReport/Program.cs`
- Create: `tools/CoverageReport/CoverageParser.cs`
- Create: `tools/CoverageReport/CoverageCalculator.cs`
- Create: `tools/CoverageReport/MarkdownRenderer.cs`
- Create: `tools/CoverageReport/BadgeGenerator.cs`
- Create: `tools/CoverageReport/GitHubCommentPoster.cs`
- Modify: `SluiceBase.slnx` (add project reference)

- [ ] **Step 1: Create the project**

```bash
mkdir -p tools/CoverageReport
dotnet new console -n CoverageReport -o tools/CoverageReport --framework net10.0
dotnet sln SluiceBase.slnx add tools/CoverageReport/CoverageReport.csproj
```

- [ ] **Step 2: Configure csproj with ReportGenerator.Core dependency**

Replace `tools/CoverageReport/CoverageReport.csproj` contents with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ReportGenerator.Core" Version="5.5.10" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Write `CoverageParser.cs`**

Create `tools/CoverageReport/CoverageParser.cs`. This is a thin adapter over ReportGenerator.Core's parser that returns our simplified domain model:

```csharp
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
        var files = new List<FileCoverage>();

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
                        var status = file.LineVisitStatus[line];
                        if (status == LineVisitStatus.NotCovered)
                            uncoveredLines.Add(line);
                    }

                    var rate = file.CoverableLines > 0
                        ? (double)file.CoveredLines / file.CoverableLines * 100
                        : 100.0;

                    var existing = files.FindIndex(f => f.Filename == file.Path);
                    if (existing >= 0)
                    {
                        var prev = files[existing];
                        var combinedUncovered = prev.UncoveredLines.Concat(uncoveredLines).Distinct().ToList();
                        var combinedCoverable = prev.UncoveredLines.Count + (int)Math.Round(
                            prev.LineRate / 100 * prev.UncoveredLines.Count / (1 - prev.LineRate / 100));
                        combinedCoverable += file.CoverableLines;
                        var combinedCovered = combinedCoverable - combinedUncovered.Count;
                        var combinedRate = combinedCoverable > 0
                            ? (double)combinedCovered / combinedCoverable * 100
                            : 100.0;
                        files[existing] = new FileCoverage(file.Path, combinedRate, combinedUncovered);
                    }
                    else
                    {
                        files.Add(new FileCoverage(file.Path, rate, uncoveredLines));
                    }
                }
            }
        }

        var overallRate = totalCoverable > 0
            ? (double)totalCovered / totalCoverable * 100
            : 100.0;

        return new CoverageData(overallRate, files);
    }
}
```

- [ ] **Step 4: Write `CoverageCalculator.cs`**

Create `tools/CoverageReport/CoverageCalculator.cs`:

```csharp
namespace CoverageReport;

public record ChangeCoverageResult(double? ChangeRate, List<FileCoverage> MatchedFiles);

public static class CoverageCalculator
{
    public static ChangeCoverageResult ComputeChangeCoverage(
        List<FileCoverage> fileCoverage,
        List<string> changedFiles)
    {
        var changedSet = changedFiles
            .Select(NormalizePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matched = fileCoverage
            .Where(f => changedSet.Contains(NormalizePath(f.Filename)))
            .ToList();

        if (matched.Count == 0)
            return new ChangeCoverageResult(null, []);

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
            return "";

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
```

- [ ] **Step 5: Write `MarkdownRenderer.cs`**

Create `tools/CoverageReport/MarkdownRenderer.cs`:

```csharp
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
        sb.AppendLine($"## Coverage Report — {label}");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value | Threshold | Status |");
        sb.AppendLine("|--------|-------|-----------|--------|");
        sb.AppendLine($"| Overall line coverage | {overallRate:F1}% | {overallThreshold:F0}% | {(overallPass ? "✅" : "❌")} |");

        if (changeRate is not null)
        {
            var icon = changeRate >= changeThreshold ? "✅" : "❌";
            sb.AppendLine($"| Changed lines covered | {changeRate:F1}% | {changeThreshold:F0}% | {icon} |");
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
                sb.AppendLine($"| `{file.Filename}` | {file.LineRate:F1}% | {uncov} |");
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
                sb.AppendLine($"| `{file.Filename}` | {file.LineRate:F1}% |");
            }
        }

        return new RenderResult(sb.ToString(), failed);
    }
}
```

- [ ] **Step 6: Write `BadgeGenerator.cs`**

Create `tools/CoverageReport/BadgeGenerator.cs`:

```csharp
namespace CoverageReport;

public static class BadgeGenerator
{
    private const string Template = """
        <svg xmlns="http://www.w3.org/2000/svg" width="{0}" height="20">
          <linearGradient id="b" x2="0" y2="100%">
            <stop offset="0" stop-color="#bbb" stop-opacity=".1"/>
            <stop offset="1" stop-opacity=".1"/>
          </linearGradient>
          <clipPath id="a">
            <rect width="{0}" height="20" rx="3" fill="#fff"/>
          </clipPath>
          <g clip-path="url(#a)">
            <rect width="{1}" height="20" fill="#555"/>
            <rect x="{1}" width="{2}" height="20" fill="{3}"/>
            <rect width="{0}" height="20" fill="url(#b)"/>
          </g>
          <g fill="#fff" text-anchor="middle" font-family="DejaVu Sans,Verdana,Geneva,sans-serif" font-size="11">
            <text x="{4}" y="15" fill="#010101" fill-opacity=".3">{5}</text>
            <text x="{4}" y="14">{5}</text>
            <text x="{6}" y="15" fill="#010101" fill-opacity=".3">{7}%</text>
            <text x="{6}" y="14">{7}%</text>
          </g>
        </svg>
        """;

    public static string GetColor(double rate) => rate switch
    {
        >= 70 => "#4c1",
        >= 50 => "#dfb317",
        _ => "#e05d44"
    };

    public static string Generate(string label, double rate)
    {
        var color = GetColor(rate);
        var value = $"{rate:F1}";
        var labelWidth = label.Length * 7 + 10;
        var valueWidth = value.Length * 7 + 18;
        var totalWidth = labelWidth + valueWidth;

        return string.Format(
            Template,
            totalWidth,
            labelWidth,
            valueWidth,
            color,
            labelWidth / 2.0,
            label,
            labelWidth + valueWidth / 2.0,
            value);
    }
}
```

- [ ] **Step 7: Write `GitHubCommentPoster.cs`**

Create `tools/CoverageReport/GitHubCommentPoster.cs`:

```csharp
using System.Diagnostics;

namespace CoverageReport;

public static class GitHubCommentPoster
{
    public static List<string> GetChangedFiles(string prNumber)
    {
        var result = RunGh($"pr diff {prNumber} --name-only");
        return result
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    public static void PostOrUpdateComment(string prNumber, string label, string body)
    {
        var marker = $"<!-- coverage-{label} -->";

        var existing = RunGh(
            $"pr view {prNumber} --json comments --jq " +
            $"'.comments[] | select(.body | startswith(\"{marker}\")) | .id'");

        var commentId = existing.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        if (!string.IsNullOrEmpty(commentId))
        {
            RunGh($"api --method PATCH repos/{{owner}}/{{repo}}/issues/comments/{commentId} -f body={body}");
        }
        else
        {
            RunGh($"pr comment {prNumber} --body {body}", useShell: true, shellBody: body);
        }
    }

    private static string RunGh(string arguments, bool useShell = false, string? shellBody = null)
    {
        ProcessStartInfo psi;

        if (useShell && shellBody is not null)
        {
            psi = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = $"pr comment {arguments.Split(' ')[2]} --body-file -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
        }

        using var process = Process.Start(psi)!;

        if (useShell && shellBody is not null)
        {
            process.StandardInput.Write(shellBody);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}
```

- [ ] **Step 8: Write `Program.cs`**

Replace `tools/CoverageReport/Program.cs` with:

```csharp
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
    double overallThreshold = 70;
    double changeThreshold = 80;
    string? prNumber = null;
    int lowestCount = 5;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--cobertura-path": coberturaPath = args[++i]; break;
            case "--label": label = args[++i]; break;
            case "--overall-threshold": overallThreshold = double.Parse(args[++i]); break;
            case "--change-threshold": changeThreshold = double.Parse(args[++i]); break;
            case "--pr-number": prNumber = args[++i]; break;
            case "--lowest-files-count": lowestCount = int.Parse(args[++i]); break;
        }
    }

    if (coberturaPath is null || label is null || prNumber is null)
    {
        Console.Error.WriteLine("Required: --cobertura-path, --label, --pr-number");
        Environment.Exit(1);
    }

    var data = CoverageParser.Parse(coberturaPath);
    var changedFiles = GitHubCommentPoster.GetChangedFiles(prNumber);
    var changeCoverage = CoverageCalculator.ComputeChangeCoverage(data.Files, changedFiles);

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
            Console.Error.WriteLine($"  Changed: {changeCoverage.ChangeRate:F1}% (threshold: {changeThreshold:F0}%)");
        Environment.Exit(1);
    }

    Console.WriteLine($"PASS: Coverage thresholds met for {label}");
    Console.WriteLine($"  Overall: {data.OverallLineRate:F1}% (threshold: {overallThreshold:F0}%)");
    if (changeCoverage.ChangeRate is not null)
        Console.WriteLine($"  Changed: {changeCoverage.ChangeRate:F1}% (threshold: {changeThreshold:F0}%)");
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
        Directory.CreateDirectory(dir);

    File.WriteAllText(output, svg);
    Console.WriteLine($"Badge generated: {label} = {data.OverallLineRate:F1}% → {output}");
}
```

- [ ] **Step 9: Verify it builds**

```bash
dotnet build tools/CoverageReport/CoverageReport.csproj
```

Expected: Build succeeds with no errors.

- [ ] **Step 10: Commit**

```bash
git add tools/CoverageReport/ SluiceBase.slnx
git commit -m "feat: add CoverageReport console tool for PR coverage reporting and badges"
```

---

### Task 3: Write tests for the CoverageReport tool

**Files:**
- Create: `tests/CoverageReport.Tests/CoverageReport.Tests.csproj`
- Create: `tests/CoverageReport.Tests/CoverageParserTests.cs`
- Create: `tests/CoverageReport.Tests/CoverageCalculatorTests.cs`
- Create: `tests/CoverageReport.Tests/MarkdownRendererTests.cs`
- Create: `tests/CoverageReport.Tests/BadgeGeneratorTests.cs`
- Create: `tests/CoverageReport.Tests/Fixtures/sample-cobertura.xml`
- Modify: `SluiceBase.slnx` (add test project)

- [ ] **Step 1: Create the test project**

```bash
mkdir -p tests/CoverageReport.Tests
dotnet new xunit -n CoverageReport.Tests -o tests/CoverageReport.Tests --framework net10.0
dotnet sln SluiceBase.slnx add tests/CoverageReport.Tests/CoverageReport.Tests.csproj
```

- [ ] **Step 2: Add project reference and configure the csproj**

Replace `tests/CoverageReport.Tests/CoverageReport.Tests.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\tools\CoverageReport\CoverageReport.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.5.1" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="xunit.v3" Version="3.2.2" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="Fixtures/**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create the test fixture**

Create `tests/CoverageReport.Tests/Fixtures/sample-cobertura.xml`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="0.75" branch-rate="0.5" version="1.0" timestamp="1234567890">
  <packages>
    <package name="SluiceBase.Api">
      <classes>
        <class name="QueryEndpoint" filename="src/SluiceBase.Api/Endpoints/QueryEndpoint.cs" line-rate="0.8" branch-rate="0.5">
          <lines>
            <line number="10" hits="1"/>
            <line number="11" hits="1"/>
            <line number="12" hits="1"/>
            <line number="13" hits="1"/>
            <line number="14" hits="0"/>
          </lines>
        </class>
        <class name="UpdateEndpoint" filename="src/SluiceBase.Api/Endpoints/UpdateEndpoint.cs" line-rate="0.5" branch-rate="0.5">
          <lines>
            <line number="20" hits="1"/>
            <line number="21" hits="0"/>
            <line number="22" hits="0"/>
            <line number="23" hits="1"/>
          </lines>
        </class>
        <class name="HealthEndpoint" filename="src/SluiceBase.Api/Endpoints/HealthEndpoint.cs" line-rate="1.0" branch-rate="1.0">
          <lines>
            <line number="5" hits="1"/>
            <line number="6" hits="1"/>
            <line number="7" hits="1"/>
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
```

- [ ] **Step 4: Write `CoverageParserTests.cs`**

Create `tests/CoverageReport.Tests/CoverageParserTests.cs`:

```csharp
using CoverageReport;

namespace CoverageReport.Tests;

public class CoverageParserTests
{
    private readonly string _fixturePath = Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "sample-cobertura.xml");

    [Fact]
    public void Parse_ReturnsCorrectOverallRate()
    {
        var result = CoverageParser.Parse(_fixturePath);
        Assert.Equal(75.0, result.OverallLineRate);
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
        Assert.Equal(80.0, query.LineRate);
        Assert.Equal([14], query.UncoveredLines);

        var update = result.Files.Single(f => f.Filename.Contains("UpdateEndpoint"));
        Assert.Equal(50.0, update.LineRate);
        Assert.Equal([21, 22], update.UncoveredLines.Order().ToList());

        var health = result.Files.Single(f => f.Filename.Contains("HealthEndpoint"));
        Assert.Equal(100.0, health.LineRate);
        Assert.Empty(health.UncoveredLines);
    }
}
```

- [ ] **Step 5: Write `CoverageCalculatorTests.cs`**

Create `tests/CoverageReport.Tests/CoverageCalculatorTests.cs`:

```csharp
using CoverageReport;

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
```

- [ ] **Step 6: Write `MarkdownRendererTests.cs`**

Create `tests/CoverageReport.Tests/MarkdownRendererTests.cs`:

```csharp
using CoverageReport;

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
```

- [ ] **Step 7: Write `BadgeGeneratorTests.cs`**

Create `tests/CoverageReport.Tests/BadgeGeneratorTests.cs`:

```csharp
using CoverageReport;

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
        Assert.StartsWith("<svg", svg);
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
```

- [ ] **Step 8: Run the tests**

```bash
dotnet test tests/CoverageReport.Tests
```

Expected: All tests pass.

- [ ] **Step 9: Commit**

```bash
git add tests/CoverageReport.Tests/ SluiceBase.slnx
git commit -m "test: add unit tests for CoverageReport tool"
```

---

### Task 4: Update `pr-checks.yml` with coverage reporting steps

**Files:**
- Modify: `.github/workflows/pr-checks.yml`

- [ ] **Step 1: Update top-level permissions**

Change the `permissions` block from:

```yaml
permissions:
  contents: read
```

to:

```yaml
permissions:
  contents: read
  pull-requests: write
```

- [ ] **Step 2: Add coverage report step to backend job**

Add the following step after the existing "Report test results" step (after the `dorny/test-reporter` step):

```yaml
      - name: Report coverage
        if: always() && github.event_name == 'pull_request'
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          dotnet run --project tools/CoverageReport -- report \
            --cobertura-path "$(find TestResults -name 'coverage.cobertura.xml' | head -1)" \
            --label "Backend" \
            --overall-threshold 70 \
            --change-threshold 80 \
            --pr-number ${{ github.event.pull_request.number }}
```

- [ ] **Step 3: Add coverage report step to frontend job**

Add the following step after the existing "Run tests" step:

```yaml
      - name: Set up .NET
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 10.0.x

      - name: Report coverage
        if: always() && github.event_name == 'pull_request'
        env:
          GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          dotnet run --project tools/CoverageReport -- report \
            --cobertura-path src/frontend/coverage/cobertura-coverage.xml \
            --label "Frontend" \
            --overall-threshold 70 \
            --change-threshold 80 \
            --pr-number ${{ github.event.pull_request.number }}
```

- [ ] **Step 4: Verify the workflow file is valid**

```bash
python3 -c "
with open('.github/workflows/pr-checks.yml') as f:
    content = f.read()
    checks = ['Report coverage', 'pull-requests: write', 'dotnet run --project tools/CoverageReport']
    for c in checks:
        assert c in content, f'Missing: {c}'
    print('All expected content present')
"
```

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/pr-checks.yml
git commit -m "feat: add coverage reporting steps to PR checks workflow"
```

---

### Task 5: Create the coverage badges workflow

**Files:**
- Create: `.github/workflows/coverage-badges.yml`

- [ ] **Step 1: Write the workflow file**

Create `.github/workflows/coverage-badges.yml`:

```yaml
name: Coverage badges

on:
  push:
    branches: [main]
  workflow_dispatch:

permissions:
  contents: write

jobs:
  badges:
    name: Generate coverage badges
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v6

      - name: Set up .NET
        uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 10.0.x

      - name: Set up Node.js
        uses: actions/setup-node@v6
        with:
          node-version: 24
          cache: npm
          cache-dependency-path: src/frontend/package-lock.json

      - name: Trust ASP.NET Core HTTPS development certificate
        run: |
          dotnet dev-certs https --clean
          dotnet dev-certs https --export-path /tmp/aspnetcore-dev-cert.crt --format PEM
          sudo cp /tmp/aspnetcore-dev-cert.crt /usr/local/share/ca-certificates/aspnetcore-dev-cert.crt
          sudo update-ca-certificates

      - name: Restore and build backend
        run: |
          dotnet restore SluiceBase.slnx
          dotnet build SluiceBase.slnx --configuration Release --no-restore

      - name: Run backend tests with coverage
        run: |
          dotnet test tests/IntegrationTests \
            --collect:"XPlat Code Coverage" \
            --configuration Release \
            --no-build

      - name: Install frontend dependencies
        working-directory: src/frontend
        run: npm ci

      - name: Run frontend tests with coverage
        working-directory: src/frontend
        run: npm run test

      - name: Generate badges
        run: |
          dotnet run --project tools/CoverageReport -- badge \
            --cobertura-path "$(find TestResults -name 'coverage.cobertura.xml' | head -1)" \
            --label "backend coverage" \
            --output badges/backend-coverage.svg
          dotnet run --project tools/CoverageReport -- badge \
            --cobertura-path src/frontend/coverage/cobertura-coverage.xml \
            --label "frontend coverage" \
            --output badges/frontend-coverage.svg

      - name: Deploy badges to gh-pages
        run: |
          git config user.name "github-actions[bot]"
          git config user.email "github-actions[bot]@users.noreply.github.com"

          # Save badges to a temp location before switching branches
          cp -r badges /tmp/coverage-badges

          # Set up gh-pages branch
          if git fetch origin gh-pages:gh-pages 2>/dev/null; then
            git checkout gh-pages
          else
            git checkout --orphan gh-pages
            git rm -rf . 2>/dev/null || true
          fi

          # Copy badges into the branch
          mkdir -p badges
          cp -f /tmp/coverage-badges/*.svg badges/
          git add badges/
          git diff --cached --quiet && echo "No badge changes" && exit 0
          git commit -m "Update coverage badges"
          git push origin gh-pages
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/coverage-badges.yml
git commit -m "feat: add coverage badges workflow for main branch"
```

---

### Task 6: Add badges to README and finalize

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Add badge images to README**

Add the following two lines immediately after the `# SluiceBase` heading (line 1) and before the description paragraph. Insert a blank line between the heading and badges:

```markdown
# SluiceBase

![Backend Coverage](https://raw.githubusercontent.com/yeongjonglim/sluice-base/gh-pages/badges/backend-coverage.svg)
![Frontend Coverage](https://raw.githubusercontent.com/yeongjonglim/sluice-base/gh-pages/badges/frontend-coverage.svg)

SluiceBase is a self-hosted database query gateway...
```

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add coverage badges to README"
```

---

### Task 7: End-to-end local verification

- [ ] **Step 1: Run frontend tests with coverage**

From `src/frontend/`:

```bash
npm run test
```

Expected: Tests pass, `coverage/cobertura-coverage.xml` is generated, text summary printed.

- [ ] **Step 2: Run backend tests with coverage**

From repo root:

```bash
dotnet test tests/IntegrationTests --collect:"XPlat Code Coverage" --configuration Release
```

Expected: Tests pass, `TestResults/{guid}/coverage.cobertura.xml` is generated.

- [ ] **Step 3: Test the report command with real data**

```bash
dotnet run --project tools/CoverageReport -- report \
  --cobertura-path "$(find TestResults -name 'coverage.cobertura.xml' | head -1)" \
  --label "Backend" \
  --overall-threshold 70 \
  --change-threshold 80 \
  --pr-number 88 \
  2>&1 || echo "Exit code: $? (expected if threshold not met or gh auth missing)"
```

Expected: Script parses the XML and attempts to post a PR comment. May fail on `gh pr diff` if not authenticated, but should show the pass/fail threshold summary.

- [ ] **Step 4: Test the badge command with real data**

```bash
dotnet run --project tools/CoverageReport -- badge \
  --cobertura-path "$(find TestResults -name 'coverage.cobertura.xml' | head -1)" \
  --label "backend coverage" \
  --output /tmp/backend-coverage.svg
cat /tmp/backend-coverage.svg
```

Expected: Valid SVG output with the coverage percentage and appropriate color.

- [ ] **Step 5: Run all CoverageReport tests**

```bash
dotnet test tests/CoverageReport.Tests
```

Expected: All tests pass.

- [ ] **Step 6: Run full solution build**

```bash
dotnet build SluiceBase.slnx
```

Expected: Entire solution builds without errors.

- [ ] **Step 7: Push and verify PR checks**

```bash
git push
```

Open PR #88 and verify the CI runs. Check that:
- Backend coverage report step runs and posts a comment
- Frontend coverage report step runs and posts a comment
- Both steps succeed or fail with clear threshold messaging
