using System.Diagnostics;

namespace NugetSync.Cli.Services;

public static class GitInfoProvider
{
    public static string? GetProjectUrl(string repoRoot)
    {
        var fromGit = RunGit(repoRoot, "remote get-url origin");
        if (!string.IsNullOrWhiteSpace(fromGit))
        {
            return fromGit.Trim();
        }

        var gitConfig = Path.Combine(repoRoot, ".git", "config");
        if (!File.Exists(gitConfig))
        {
            return null;
        }

        var lines = File.ReadAllLines(gitConfig);
        var inOrigin = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[remote \"origin\"]", StringComparison.OrdinalIgnoreCase))
            {
                inOrigin = true;
                continue;
            }

            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                inOrigin = false;
                continue;
            }

            if (inOrigin && trimmed.StartsWith("url", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2)
                {
                    return parts[1].Trim();
                }
            }
        }

        return null;
    }

    public static string? GetRepoRef(string repoRoot)
    {
        var branch = RunGit(repoRoot, "rev-parse --abbrev-ref HEAD")?.Trim();
        if (!string.IsNullOrWhiteSpace(branch) && !string.Equals(branch, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return branch;
        }

        var tag = RunGit(repoRoot, "describe --tags --exact-match")?.Trim();
        if (!string.IsNullOrWhiteSpace(tag))
        {
            return tag;
        }

        var sha = RunGit(repoRoot, "rev-parse --short HEAD")?.Trim();
        return string.IsNullOrWhiteSpace(sha) ? null : sha;
    }

    private static string? RunGit(string repoRoot, string args)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
