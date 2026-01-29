using System.Text.Json;
using CommandLine;
using NugetSync.Cli.Models;
using Serilog;

namespace NugetSync.Cli.Services;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();

        try
        {
            return await RunAsyncInternal(args);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unhandled error");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static async Task<int> RunAsyncInternal(string[] args)
    {
        var parser = new Parser(with => with.HelpWriter = Console.Out);
        var result = parser.ParseArguments<InitOptions, RunOptions, RunAllOptions, MergeOptions, ListOptions, RulesOptions, InteractiveOptions, UpdateOptions>(args);

        return await result.MapResult(
            (InitOptions opts) => RunInitAsync(opts),
            (RunOptions opts) => RunRunAsync(opts),
            (RunAllOptions opts) => RunAllAndMergeAsync(opts),
            (MergeOptions _) => Task.FromResult(RunMerge()),
            (ListOptions _) => Task.FromResult(ListParsedRepos()),
            (RulesOptions opts) => RunRulesAsync(opts),
            (InteractiveOptions _) => RunInteractiveAsync(),
            (UpdateOptions opts) => Task.FromResult(RunUpdate(opts)),
            errs => Task.FromResult(HandleParseErrors(errs)));
    }

    private static Task<int> RunInitAsync(InitOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.DataRoot))
        {
            Console.Error.WriteLine("Missing --data-root. Example: NugetSync init --data-root \"D:\\NugetSyncData\"");
            return Task.FromResult(2);
        }

            SettingsStore.Save(new Settings { DataRoot = opts.DataRoot });
            Log.Information("Settings saved.");
        return Task.FromResult(0);
    }

    private static Task<int> RunRunAsync(RunOptions opts)
    {
        var settings = SettingsStore.LoadOrThrow();
        var repoRoot = NormalizeRepoRoot(opts.Repo ?? Directory.GetCurrentDirectory());
        Log.Information("Running analysis for {RepoRoot}", repoRoot);
        var dataRoot = settings.DataRoot;
        var rulesPath = string.IsNullOrWhiteSpace(opts.Rules)
            ? Path.Combine(dataRoot, "nugetsyncrules.json")
            : opts.Rules;

        var outputPath = string.IsNullOrWhiteSpace(opts.Output)
            ? GetDefaultOutputPath(dataRoot, repoRoot)
            : opts.Output;

        var inventoryPath = string.IsNullOrWhiteSpace(opts.Inventory)
            ? GetDefaultInventoryPath(dataRoot, repoRoot)
            : opts.Inventory;

        return RunForRepoAsync(repoRoot, rulesPath, outputPath, inventoryPath, opts.IncludeTransitive, opts.Force);
    }

    private static async Task<int> RunAllAndMergeAsync(RunAllOptions opts)
    {
        var runAllResult = await RunAllAsync(opts.Force);
        if (runAllResult != 0)
        {
            return runAllResult;
        }

        return RunMerge();
    }

    private static Task<int> RunRulesAsync(RulesOptions opts)
    {
        if (string.Equals(opts.Action, "add", StringComparison.OrdinalIgnoreCase))
        {
            var settings = SettingsStore.LoadOrThrow();
            Log.Information("Starting interactive rule add");
            RulesWizard.AddRuleInteractive(settings.DataRoot);
            return Task.FromResult(0);
        }

        if (string.Equals(opts.Action, "add-mass", StringComparison.OrdinalIgnoreCase))
        {
            var settings = SettingsStore.LoadOrThrow();
            Log.Information("Starting mass rule add");
            RulesWizard.AddRulesMassInteractive(settings.DataRoot);
            return Task.FromResult(0);
        }

        Log.Error("Unknown rules action. Use: rules add | rules add-mass");
        return Task.FromResult(2);
    }

    private static int HandleParseErrors(IEnumerable<Error> errors)
    {
        if (errors.Any(e => e is HelpRequestedError or VersionRequestedError))
        {
            return 0;
        }

        return 2;
    }

    private static async Task<int> RunAllAsync(bool force)
    {
        var settings = SettingsStore.LoadOrThrow();
        var repoRoots = LoadInventoryRepoRoots(settings).ToList();
        if (repoRoots.Count == 0)
        {
            Log.Error("No parsed repositories found. Run NugetSync at least once.");
            return 2;
        }

        var exitCode = 0;
        foreach (var repoRoot in repoRoots)
        {
            var normalized = NormalizeRepoRoot(repoRoot);
            Log.Information("Running analysis for {RepoRoot}", normalized);
            var rulesPath = Path.Combine(settings.DataRoot, "nugetsyncrules.json");
            var outputPath = GetDefaultOutputPath(settings.DataRoot, normalized);
            var inventoryPath = GetDefaultInventoryPath(settings.DataRoot, normalized);

            var runResult = await RunForRepoAsync(normalized, rulesPath, outputPath, inventoryPath, includeTransitive: true, force: force);
            if (runResult != 0)
            {
                exitCode = runResult;
            }
        }

        return exitCode;
    }

    private static async Task<int> RunInteractiveAsync()
    {
        Log.Information("Paste repo paths (one per line). Type 'done' to run.");
        var repos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var line = Console.ReadLine();
            if (line == null)
            {
                break;
            }

            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (string.Equals(trimmed, "done", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            repos.Add(trimmed);
        }

        if (repos.Count == 0)
        {
            Log.Error("No directories provided.");
            return 2;
        }

        var settings = SettingsStore.LoadOrThrow();
        var exitCode = 0;
        foreach (var repoRoot in repos)
        {
            var normalized = NormalizeRepoRoot(repoRoot);
            Log.Information("Running analysis for {RepoRoot}", normalized);
            var rulesPath = Path.Combine(settings.DataRoot, "nugetsyncrules.json");
            var outputPath = GetDefaultOutputPath(settings.DataRoot, normalized);
            var inventoryPath = GetDefaultInventoryPath(settings.DataRoot, normalized);

            var runResult = await RunForRepoAsync(normalized, rulesPath, outputPath, inventoryPath, includeTransitive: true, force: false);
            if (runResult != 0)
            {
                exitCode = runResult;
            }
        }

        if (exitCode != 0)
        {
            return exitCode;
        }

        return RunMerge();
    }

    private static int RunMerge()
    {
        var settings = SettingsStore.LoadOrThrow();
        var outputsRoot = Path.Combine(settings.DataRoot, "outputs");
        var outputPath = Path.Combine(outputsRoot, "NugetSync.MegaReport.tsv");
        MegaReportMerger.MergeReports(outputsRoot, outputPath);
        Log.Information("Mega report: {OutputPath}", outputPath);
        return 0;
    }

    private static int ListParsedRepos()
    {
        var settings = SettingsStore.LoadOrThrow();
        var repoRoots = LoadInventoryRepoRoots(settings).ToList();
        if (repoRoots.Count == 0)
        {
            Log.Information("No parsed repositories found.");
            return 0;
        }

        foreach (var repoRoot in repoRoots)
        {
            Console.WriteLine(repoRoot);
        }

        return 0;
    }

    private static int RunUpdate(UpdateOptions opts)
    {
        var settings = SettingsStore.LoadOrThrow();
        var repoRoots = LoadInventoryRepoRoots(settings).ToList();
        if (repoRoots.Count == 0)
        {
            Log.Error("No parsed repositories found. Run NugetSync at least once.");
            return 2;
        }

        var failedRepos = new List<(string repo, string error)>();

        foreach (var repoRoot in repoRoots)
        {
            var normalized = NormalizeRepoRoot(repoRoot);

            if (GitInfoProvider.HasUncommittedChanges(normalized))
            {
                Log.Information("Skipping {RepoRoot}: uncommitted changes.", normalized);
                continue;
            }

            var (checkoutOk, checkoutErr) = GitInfoProvider.Checkout(normalized, opts.Branch);
            if (!checkoutOk)
            {
                Log.Error("Update failed for {RepoRoot}: checkout - {Error}", normalized, checkoutErr);
                failedRepos.Add((normalized, "checkout: " + (checkoutErr ?? "unknown")));
                continue;
            }

            var (pullOk, pullErr) = GitInfoProvider.Pull(normalized);
            if (!pullOk)
            {
                Log.Error("Update failed for {RepoRoot}: pull - {Error}", normalized, pullErr);
                failedRepos.Add((normalized, "pull: " + (pullErr ?? "unknown")));
                continue;
            }

            Log.Information("Updated {RepoRoot} on {Branch}.", normalized, opts.Branch);
        }

        if (failedRepos.Count > 0)
        {
            Log.Warning("Repositories where update failed:");
            foreach (var (repo, error) in failedRepos)
            {
                Console.Error.WriteLine("  {0}: {1}", repo, error);
            }
            return 1;
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
        bool includeTransitive,
        bool force)
    {
        var repoKey = PathHelpers.GetRepoKey(repoRoot);
        var outputDir = Path.GetDirectoryName(outputPath)!;
        var inventoryDir = Path.GetDirectoryName(inventoryPath)!;

        Directory.CreateDirectory(outputDir);
        Directory.CreateDirectory(inventoryDir);

        var projectUrl = GitInfoProvider.GetProjectUrl(repoRoot);
        var branchName = GitInfoProvider.GetRepoRef(repoRoot) ?? string.Empty;
        var commitSha = GitInfoProvider.GetCommitSha(repoRoot) ?? string.Empty;

        var rulesModel = RuleEngine.LoadRules(rulesPath);
        var listTargets = DotnetListPackageRunner.DiscoverTargets(repoRoot);
        var inventory = new RepoInventory
        {
            RepoRoot = Path.GetFullPath(repoRoot),
            ProjectUrl = projectUrl ?? string.Empty,
            RepoRef = branchName,
            BranchName = branchName,
            CommitSha = commitSha,
            GeneratedAtUtc = DateTime.UtcNow,
            Projects = new List<ProjectInventory>()
        };

        if (!force && ShouldSkipAnalysis(inventoryPath, branchName, commitSha))
        {
            Log.Information("Skipping analysis for {RepoRoot} (unchanged branch/commit).", repoRoot);
            return 0;
        }

        if (listTargets.Count == 0)
        {
            Log.Error("No .sln or .csproj found in repo: {RepoRoot}", repoRoot);
            InventoryWriter.Write(inventoryPath, inventory);
            return 2;
        }

        var exitCode = 0;
        foreach (var target in listTargets)
        {
            try
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
            catch (Exception ex)
            {
                exitCode = 2;
                Log.Error(ex, "Failed to restore/list {Target}", target);
                inventory.Projects.Add(new ProjectInventory
                {
                    CsprojPath = PathHelpers.ToRepoRelativePath(repoRoot, target)
                });
            }
        }

        InventoryWriter.Write(inventoryPath, inventory);

        var rows = RuleEngine.BuildReportRows(inventory, rulesModel);
        ReportWriter.WriteTsv(outputPath, rows);

        Log.Information("Report: {ReportPath}", outputPath);
        Log.Information("Inventory: {InventoryPath}", inventoryPath);
        return exitCode;
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

    private static bool ShouldSkipAnalysis(string inventoryPath, string branchName, string commitSha)
    {
        if (string.IsNullOrWhiteSpace(branchName) || string.IsNullOrWhiteSpace(commitSha))
        {
            return false;
        }

        if (!File.Exists(inventoryPath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(inventoryPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("BranchName", out var branchElement) ||
                !root.TryGetProperty("CommitSha", out var shaElement))
            {
                return false;
            }

            var prevBranch = branchElement.GetString();
            var prevSha = shaElement.GetString();
            return string.Equals(prevBranch, branchName, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(prevSha, commitSha, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    [Verb("init", HelpText = "Initialize settings.")]
    private sealed class InitOptions
    {
        [Option("data-root", Required = true, HelpText = "Data root for rules and outputs.")]
        public string DataRoot { get; set; } = string.Empty;
    }

    [Verb("run", HelpText = "Run analysis for a repository.")]
    private sealed class RunOptions
    {
        [Option("repo", HelpText = "Repository root to scan. Defaults to current directory.")]
        public string? Repo { get; set; }

        [Option("rules", HelpText = "Path to rules JSON.")]
        public string? Rules { get; set; }

        [Option("output", HelpText = "Output TSV path.")]
        public string? Output { get; set; }

        [Option("inventory", HelpText = "Inventory JSON path.")]
        public string? Inventory { get; set; }

        [Option("include-transitive", Default = true, HelpText = "Include transitive packages.")]
        public bool IncludeTransitive { get; set; } = true;

        [Option("force", Default = false, HelpText = "Force analysis even if unchanged.")]
        public bool Force { get; set; }
    }

    [Verb("run-all", HelpText = "Run analysis against all parsed repo roots.")]
    private sealed class RunAllOptions
    {
        [Option("force", Default = false, HelpText = "Force analysis even if unchanged.")]
        public bool Force { get; set; }
    }

    [Verb("merge", HelpText = "Merge all report TSV files into a mega report.")]
    private sealed class MergeOptions
    {
    }

    [Verb("list", HelpText = "List parsed repo roots from inventory.")]
    private sealed class ListOptions
    {
    }

    [Verb("interactive", HelpText = "Paste repo roots, end with 'done', then run and merge.")]
    private sealed class InteractiveOptions
    {
    }

    [Verb("rules", HelpText = "Rule management.")]
    private sealed class RulesOptions
    {
        [Value(0, Required = true, HelpText = "Action (add | add-mass).")]
        public string Action { get; set; } = string.Empty;
    }

    [Verb("update", HelpText = "Switch to branch and pull for all clean repos.")]
    private sealed class UpdateOptions
    {
        [Option("branch", Default = "develop", HelpText = "Branch to checkout.")]
        public string Branch { get; set; } = "develop";
    }
}
