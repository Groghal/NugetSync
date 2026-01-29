using System.Text.Json;
using NugetSync.Cli.Models;

namespace NugetSync.Cli.Services;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return await RunAsyncInternal(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            PrintHelp();
            return 1;
        }
    }

    private static async Task<int> RunAsyncInternal(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase)))
        {
            PrintHelp();
            return 0;
        }

        if (args.Length > 0 && string.Equals(args[0], "init", StringComparison.OrdinalIgnoreCase))
        {
            var dataRoot = GetOptionValue(args, "--data-root");
            if (string.IsNullOrWhiteSpace(dataRoot))
            {
                Console.Error.WriteLine("Missing --data-root. Example: NugetSync init --data-root \"D:\\NugetSyncData\"");
                PrintHelp();
                return 2;
            }

            SettingsStore.Save(new Settings { DataRoot = dataRoot });
            Console.WriteLine("Settings saved.");
            return 0;
        }

        if (args.Length > 0 && string.Equals(args[0], "run-all", StringComparison.OrdinalIgnoreCase))
        {
            var runAllResult = await RunAllAsync();
            if (runAllResult != 0)
            {
                return runAllResult;
            }

            return RunMerge();
        }

        if (args.Length > 0 && string.Equals(args[0], "list", StringComparison.OrdinalIgnoreCase))
        {
            return ListParsedRepos();
        }

        if (args.Length > 0 && string.Equals(args[0], "merge", StringComparison.OrdinalIgnoreCase))
        {
            return RunMerge();
        }

        if (args.Length > 0 && string.Equals(args[0], "run", StringComparison.OrdinalIgnoreCase))
        {
            var remaining = args.Skip(1).ToArray();
            return await RunDefaultAsync(remaining);
        }

        if (args.Length > 1 &&
            string.Equals(args[0], "rules", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "add", StringComparison.OrdinalIgnoreCase))
        {
            var settings = SettingsStore.LoadOrThrow();
            RulesWizard.AddRuleInteractive(settings.DataRoot);
            return 0;
        }

        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        if (args.Length > 0 && !args[0].StartsWith("-", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Unknown command or arguments.");
            PrintHelp();
            return 2;
        }

        return await RunDefaultAsync(args);
    }

    private static async Task<int> RunDefaultAsync(string[] args)
    {
        var settings = SettingsStore.LoadOrThrow();
        var repoRoot = NormalizeRepoRoot(GetOptionValue(args, "--repo") ?? Directory.GetCurrentDirectory());
        var rulesOverride = GetOptionValue(args, "--rules");
        var outputOverride = GetOptionValue(args, "--output");
        var inventoryOverride = GetOptionValue(args, "--inventory");
        var includeTransitive = GetBoolOptionValue(args, "--include-transitive", defaultValue: true);

        var dataRoot = settings.DataRoot;

        var rulesPath = rulesOverride ?? Path.Combine(dataRoot, "nugetsyncrules.json");
        var outputPath = outputOverride ?? GetDefaultOutputPath(dataRoot, repoRoot);
        var inventoryPath = inventoryOverride ?? GetDefaultInventoryPath(dataRoot, repoRoot);

        return await RunForRepoAsync(repoRoot, rulesPath, outputPath, inventoryPath, includeTransitive);
    }

    private static string? GetOptionValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (current.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }

            if (current.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return current[(name.Length + 1)..];
            }
        }

        return null;
    }

    private static bool GetBoolOptionValue(string[] args, string name, bool defaultValue)
    {
        var value = GetOptionValue(args, name);
        if (value is null)
        {
            return defaultValue;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("NugetSync");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  init --data-root <path>     Initialize settings");
        Console.WriteLine("  rules add                   Add a package rule (interactive)");
        Console.WriteLine("  list                        List parsed repo roots (from inventory)");
        Console.WriteLine("  merge                       Merge all reports into one mega report");
        Console.WriteLine("  run                         Run analysis (use options below)");
        Console.WriteLine("  run-all                     Run against all parsed repo roots");
        Console.WriteLine();
        Console.WriteLine("Run options:");
        Console.WriteLine("  --repo <path>");
        Console.WriteLine("  --rules <path>");
        Console.WriteLine("  --output <path>");
        Console.WriteLine("  --inventory <path>");
        Console.WriteLine("  --include-transitive true|false");
        Console.WriteLine();
        Console.WriteLine("Help:");
        Console.WriteLine("  -h, --help");
    }

    private static async Task<int> RunAllAsync()
    {
        var settings = SettingsStore.LoadOrThrow();
        var repoRoots = LoadInventoryRepoRoots(settings).ToList();
        if (repoRoots.Count == 0)
        {
            Console.Error.WriteLine("No parsed repositories found. Run NugetSync at least once.");
            return 2;
        }

        var exitCode = 0;
        foreach (var repoRoot in repoRoots)
        {
            var normalized = NormalizeRepoRoot(repoRoot);
            var rulesPath = Path.Combine(settings.DataRoot, "nugetsyncrules.json");
            var outputPath = GetDefaultOutputPath(settings.DataRoot, normalized);
            var inventoryPath = GetDefaultInventoryPath(settings.DataRoot, normalized);

            var runResult = await RunForRepoAsync(normalized, rulesPath, outputPath, inventoryPath, includeTransitive: true);
            if (runResult != 0)
            {
                exitCode = runResult;
            }
        }

        return exitCode;
    }

    private static int RunMerge()
    {
        var settings = SettingsStore.LoadOrThrow();
        var outputsRoot = Path.Combine(settings.DataRoot, "outputs");
        var outputPath = Path.Combine(outputsRoot, "NugetSync.MegaReport.tsv");
        MegaReportMerger.MergeReports(outputsRoot, outputPath);
        Console.WriteLine($"Mega report: {outputPath}");
        return 0;
    }

    private static int ListParsedRepos()
    {
        var settings = SettingsStore.LoadOrThrow();
        var repoRoots = LoadInventoryRepoRoots(settings).ToList();
        if (repoRoots.Count == 0)
        {
            Console.WriteLine("No parsed repositories found.");
            return 0;
        }

        foreach (var repoRoot in repoRoots)
        {
            Console.WriteLine(repoRoot);
        }

        return 0;
    }

    private static IEnumerable<string> LoadInventoryRepoRoots(Settings settings)
    {
        var outputsRoot = Path.Combine(settings.DataRoot, "outputs");
        if (!Directory.Exists(outputsRoot))
        {
            return Array.Empty<string>();
        }

        var files = Directory.EnumerateFiles(outputsRoot, "NugetSync.Inventory.json", SearchOption.AllDirectories);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                if (TryReadRepoRoot(json, out var repoRoot))
                {
                    result.Add(repoRoot);
                }
            }
            catch
            {
                // Ignore malformed inventory.
            }
        }

        return result;
    }

    private static bool TryReadRepoRoot(string json, out string repoRoot)
    {
        repoRoot = string.Empty;
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("RepoRoot", out var rootElement))
        {
            return false;
        }

        var value = rootElement.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        repoRoot = value;
        return true;
    }

    private static async Task<int> RunForRepoAsync(
        string repoRoot,
        string rulesPath,
        string outputPath,
        string inventoryPath,
        bool includeTransitive)
    {
        var repoKey = PathHelpers.GetRepoKey(repoRoot);
        var outputDir = Path.GetDirectoryName(outputPath)!;
        var inventoryDir = Path.GetDirectoryName(inventoryPath)!;

        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(inventoryDir);

        var projectUrl = GitInfoProvider.GetProjectUrl(repoRoot);
        var repoRef = GitInfoProvider.GetRepoRef(repoRoot);

        var rulesModel = RuleEngine.LoadRules(rulesPath);
        var listTargets = DotnetListPackageRunner.DiscoverTargets(repoRoot);
        if (listTargets.Count == 0)
        {
            Console.Error.WriteLine($"No .sln or .csproj found in repo: {repoRoot}");
            return 2;
        }

        var inventory = new RepoInventory
        {
            RepoRoot = Path.GetFullPath(repoRoot),
            ProjectUrl = projectUrl ?? string.Empty,
            RepoRef = repoRef ?? string.Empty,
            GeneratedAtUtc = DateTime.UtcNow,
            Projects = new List<ProjectInventory>()
        };

        foreach (var target in listTargets)
        {
            await DotnetListPackageRunner.RestoreAsync(repoRoot, target);
            var json = await DotnetListPackageRunner.ListPackagesJsonAsync(repoRoot, target, includeTransitive);
            var projects = DotnetListPackageParser.Parse(json, repoRoot);
            if (projects.Count == 0)
            {
                inventory.Projects.Add(new ProjectInventory
                {
                    CsprojPath = PathHelpers.ToRepoRelativePath(repoRoot, target)
                });
                continue;
            }

            inventory.Projects.AddRange(projects);
        }

        InventoryWriter.Write(inventoryPath, inventory);

        var rows = RuleEngine.BuildReportRows(inventory, rulesModel);
        ReportWriter.WriteTsv(outputPath, rows);

        Console.WriteLine($"Report: {outputPath}");
        Console.WriteLine($"Inventory: {inventoryPath}");
        return 0;
    }

    private static string GetDefaultOutputPath(string dataRoot, string repoRoot)
    {
        var repoKey = PathHelpers.GetRepoKey(repoRoot);
        return Path.Combine(dataRoot, "outputs", repoKey, "NugetSync.Report.tsv");
    }

    private static string GetDefaultInventoryPath(string dataRoot, string repoRoot)
    {
        var repoKey = PathHelpers.GetRepoKey(repoRoot);
        return Path.Combine(dataRoot, "outputs", repoKey, "NugetSync.Inventory.json");
    }

    private static string NormalizeRepoRoot(string repoRoot)
    {
        var trimmed = repoRoot.Trim().Trim('"').TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFullPath(trimmed);
    }
}
