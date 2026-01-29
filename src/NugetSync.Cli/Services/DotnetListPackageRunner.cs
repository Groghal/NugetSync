using System.Diagnostics;

namespace NugetSync.Cli.Services;

public static class DotnetListPackageRunner
{
    public static List<string> DiscoverTargets(string repoRoot)
    {
        return Directory
            .EnumerateFiles(repoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static Task RestoreAsync(string repoRoot, string target)
        => RunDotnetAsync(repoRoot, $"restore \"{target}\"");

    public static Task<string> ListPackagesJsonAsync(string repoRoot, string target, bool includeTransitive)
    {
        var includeFlag = includeTransitive ? "--include-transitive" : string.Empty;
        return RunDotnetCaptureAsync(repoRoot, $"list \"{target}\" package --format json {includeFlag}".Trim());
    }

    private static async Task RunDotnetAsync(string repoRoot, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start dotnet process.");
        }

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"dotnet {arguments} failed: {error}");
        }
    }

    private static async Task<string> RunDotnetCaptureAsync(string repoRoot, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start dotnet process.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet {arguments} failed: {error}");
        }

        return output;
    }
}
