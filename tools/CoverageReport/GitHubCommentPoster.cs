using System.Diagnostics;

namespace CoverageReport;

public static class GitHubCommentPoster
{
    public static List<string> GetChangedFiles(string prNumber)
    {
        var result = RunGh(["pr", "diff", prNumber, "--name-only"]);
        return result
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    public static void PostOrUpdateComment(string prNumber, string label, string body)
    {
        var marker = $"<!-- coverage-{label} -->";

        var existing = RunGh([
            "api", "repos/{owner}/{repo}/issues/" + prNumber + "/comments",
            "--jq", $".[] | select(.body | startswith(\"{marker}\")) | .id"
        ]);

        var commentId = existing.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        if (!string.IsNullOrEmpty(commentId))
        {
            RunGh(["api", "--method", "PATCH",
                $"repos/{{owner}}/{{repo}}/issues/comments/{commentId}",
                "-f", $"body={body}"]);
        }
        else
        {
            PostWithBodyFile(prNumber, body);
        }
    }

    private static void PostWithBodyFile(string prNumber, string body)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "gh",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("pr");
        psi.ArgumentList.Add("comment");
        psi.ArgumentList.Add(prNumber);
        psi.ArgumentList.Add("--body-file");
        psi.ArgumentList.Add("-");

        using var process = Process.Start(psi)!;
        process.StandardInput.Write(body);
        process.StandardInput.Close();
        process.WaitForExit();
    }

    private static string RunGh(string[] arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "gh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }
}
