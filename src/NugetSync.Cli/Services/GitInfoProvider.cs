using System.Diagnostics;
using System.Text;

namespace NugetSync.Cli.Services;

public static class GitInfoProvider
{
    private static readonly object GitLock = new object();
    private const int IdleTimeoutMs = 30000;
    private const int WatchdogPollMs = 1000;
    private const int ProcessExitTimeoutMs = 10000;
    private const int StreamDrainTimeoutMs = 5000;

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

    public static string? GetCommitSha(string repoRoot)
    {
        var sha = RunGit(repoRoot, "rev-parse HEAD")?.Trim();
        return string.IsNullOrWhiteSpace(sha) ? null : sha;
    }

    public static bool HasUncommittedChanges(string repoRoot)
    {
        var output = RunGit(repoRoot, "status --porcelain");
        return !string.IsNullOrWhiteSpace(output);
    }

    public static (bool success, string? error) Checkout(string repoRoot, string branch)
    {
        var arg = "checkout \"" + branch.Replace("\"", "\\\"") + "\"";
        return RunGitWithError(repoRoot, arg);
    }

    public static (bool success, string? error) Pull(string repoRoot)
    {
        return RunGitWithError(repoRoot, "pull");
    }

    private static (bool success, string? error) RunGitWithError(string repoRoot, string args)
    {
        try
        {
            lock (GitLock)
            {
                var (stdout, stderr, exitCode) = RunGitCore(repoRoot, args);
                if (exitCode == null)
                {
                    return (false, "Failed to start git.");
                }

                if (exitCode != 0)
                {
                    var err = string.IsNullOrWhiteSpace(stderr) ? $"Exit code {exitCode}" : stderr.Trim();
                    return (false, err);
                }

                return (true, null);
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string? RunGit(string repoRoot, string args)
    {
        try
        {
            lock (GitLock)
            {
                var (stdout, _, exitCode) = RunGitCore(repoRoot, args);
                if (exitCode == null || exitCode != 0)
                {
                    return null;
                }

                return stdout;
            }
        }
        catch
        {
            return null;
        }
    }

    private static (string stdout, string stderr, int? exitCode) RunGitCore(string repoRoot, string args)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var stdoutSb = new StringBuilder();
        var stderrSb = new StringBuilder();
        var lastOutputUtc = DateTime.UtcNow;
        var lockObj = new object();

        using var stdoutDone = new ManualResetEventSlim(false);
        using var stderrDone = new ManualResetEventSlim(false);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (lockObj)
                {
                    stdoutSb.AppendLine(e.Data);
                    lastOutputUtc = DateTime.UtcNow;
                }
            }
            else
            {
                stdoutDone.Set();
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (lockObj)
                {
                    stderrSb.AppendLine(e.Data);
                    lastOutputUtc = DateTime.UtcNow;
                }
            }
            else
            {
                stderrDone.Set();
            }
        };

        try
        {
            process.Start();
        }
        catch
        {
            return (string.Empty, string.Empty, null);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var idleTimeout = TimeSpan.FromMilliseconds(IdleTimeoutMs);
        var wasKilledDueToIdle = false;
        while (!process.HasExited)
        {
            Thread.Sleep(WatchdogPollMs);
            lock (lockObj)
            {
                if (DateTime.UtcNow - lastOutputUtc > idleTimeout)
                {
                    wasKilledDueToIdle = true;
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                        // Ignore - process may have exited
                    }

                    break;
                }
            }
        }

        process.WaitForExit(ProcessExitTimeoutMs);
        stdoutDone.Wait(StreamDrainTimeoutMs);
        stderrDone.Wait(StreamDrainTimeoutMs);

        if (wasKilledDueToIdle)
        {
            stderrSb.AppendLine($"Process killed: no output for {IdleTimeoutMs / 1000} seconds (idle timeout)");
        }

        var exitCode = process.HasExited ? process.ExitCode : -1;
        return (stdoutSb.ToString(), stderrSb.ToString(), exitCode);
    }
}
