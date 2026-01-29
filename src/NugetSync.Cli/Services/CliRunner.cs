using NugetSync.Cli.Models;

namespace NugetSync.Cli.Services;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "init", StringComparison.OrdinalIgnoreCase))
        {
            var dataRoot = GetOptionValue(args, "--data-root");
            if (string.IsNullOrWhiteSpace(dataRoot))
            {
                Console.Error.WriteLine("Missing --data-root. Example: NugetSync init --data-root \"D:\\NugetSyncData\"");
                return 2;
            }

            SettingsStore.Save(new Settings { DataRoot = dataRoot });
            Console.WriteLine("Settings saved.");
            return 0;
        }

        if (args.Length > 1 &&
            string.Equals(args[0], "rules", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(args[1], "add", StringComparison.OrdinalIgnoreCase))
        {
            var settings = SettingsStore.LoadOrThrow();
            RulesWizard.AddRuleInteractive(settings.DataRoot);
            return 0;
        }

        return await RunDefaultAsync(args);
    }

    private static async Task<int> RunDefaultAsync(string[] args)
    {
        var settings = SettingsStore.LoadOrThrow();
        var repoRoot = GetOptionValue(args, "--repo") ?? Directory.GetCurrentDirectory();
        var rulesOverride = GetOptionValue(args, "--rules");
        var outputOverride = GetOptionValue(args, "--output");
        var inventoryOverride = GetOptionValue(args, "--inventory");
        var includeTransitive = GetBoolOptionValue(args, "--include-transitive", defaultValue: true);

        var repoKey = PathHelpers.GetRepoKey(repoRoot);
        var dataRoot = settings.DataRoot;

        var rulesPath = rulesOverride ?? Path.Combine(dataRoot, "nugetsyncrules.json");
        var outputPath = outputOverride ?? Path.Combine(dataRoot, "outputs", repoKey, "NugetSync.Report.tsv");
        var inventoryPath = inventoryOverride ?? Path.Combine(dataRoot, "outputs", repoKey, "NugetSync.Inventory.json");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(inventoryPath)!);

        var projectUrl = GitInfoProvider.GetProjectUrl(repoRoot);
        var repoRef = GitInfoProvider.GetRepoRef(repoRoot);

        var rulesModel = RuleEngine.LoadRules(rulesPath);
        var listTargets = DotnetListPackageRunner.DiscoverTargets(repoRoot);
        if (listTargets.Count == 0)
        {
            Console.Error.WriteLine("No .sln or .csproj found in the repo root.");
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
}
