using NugetSync.Cli.Models;
using NugetSync.Cli.Services;

namespace NugetSync.Cli.Tests;

public class UnitTest1
{
    [Fact]
    public void PathHelpers_ToRepoRelativePath_UsesForwardSlashes()
    {
        var repoRoot = GetRepoRoot();
        var filePath = Path.Combine(repoRoot, "samples", "SampleApp", "SampleApp.csproj");

        var relative = PathHelpers.ToRepoRelativePath(repoRoot, filePath);

        Assert.Equal("samples/SampleApp/SampleApp.csproj", relative);
    }

    [Fact]
    public void PathHelpers_GetRepoKey_UsesNameAndHash()
    {
        var repoRoot = GetRepoRoot();

        var key = PathHelpers.GetRepoKey(repoRoot);

        Assert.Matches("^[A-Za-z0-9_-]+_[0-9a-f]{8}$", key);
    }

    [Fact]
    public async Task DotnetListPackageParser_ParsesFrameworksAndPackages()
    {
        var repoRoot = GetRepoRoot();
        var sampleProject = Path.Combine(repoRoot, "samples", "SampleApp", "SampleApp.csproj");
        await DotnetListPackageRunner.RestoreAsync(repoRoot, sampleProject);
        var json = await DotnetListPackageRunner.ListPackagesJsonAsync(repoRoot, sampleProject, includeTransitive: false);
        var projects = DotnetListPackageParser.Parse(json, repoRoot);

        Assert.Single(projects);
        var project = projects[0];
        Assert.Equal("samples/SampleApp/SampleApp.csproj", project.CsprojPath);
        Assert.Single(project.Frameworks);
        var framework = project.Frameworks[0];
        Assert.Equal("net8.0", framework.Tfm);
        Assert.Contains(framework.Packages, p => p.Id == "Newtonsoft.Json");
        Assert.Contains(framework.Packages, p => p.Id == "Serilog");
        Assert.Contains(framework.Packages, p => p.Id == "NodaTime");
    }

    [Fact]
    public async Task RuleEngine_BuildReportRows_CoversPoliciesAndFrameworks()
    {
        var repoRoot = GetRepoRoot();
        var inventory = await BuildInventoryFromSamples(repoRoot);
        var rulesPath = Path.Combine(repoRoot, "samples", "TestRules", "nugetsyncrules.json");
        var rules = RuleEngine.LoadRules(rulesPath);

        var rows = RuleEngine.BuildReportRows(inventory, rules);

        Assert.Contains(rows, r => r.NugetName == "Newtonsoft.Json" && r.Action == "upgrade");
        Assert.Contains(rows, r => r.NugetName == "Serilog" && r.Action == "remove");
        Assert.Contains(rows, r => r.NugetName == "Dapper" && r.Action == "upgrade");
        Assert.Contains(rows, r => r.NugetName == "Polly" && r.Action == "upgrade");
        Assert.Contains(rows, r => r.NugetName == "Humanizer" && r.Action == "upgrade");
        Assert.Contains(rows, r => r.NugetName == "NodaTime" && r.Action == "upgrade");

        foreach (var row in rows)
        {
            Assert.Equal("net8.0", row.Frameworks);
            Assert.False(string.IsNullOrWhiteSpace(row.Comment));
        }
    }

    [Fact]
    public void ReportWriter_WritesHeaderWithFrameworks()
    {
        var path = Path.GetTempFileName();
        try
        {
            ReportWriter.WriteTsv(path, new List<ReportRow>());
            var header = File.ReadLines(path).First();
            Assert.Equal("ProjectUrl\tRepoRef\tCsprojPath\tFrameworks\tNugetName\tAction\tTargetVersion\tComment", header);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RuleEngine_OutputsUpToDateRows_WhenNoRulesMatch()
    {
        var repoRoot = GetRepoRoot();
        var inventory = await BuildInventoryFromSamples(repoRoot);
        var rules = new RulesFile { Packages = new List<PackageRule>() };

        var rows = RuleEngine.BuildReportRows(inventory, rules);

        Assert.Equal(inventory.Projects.Count, rows.Count);
        foreach (var row in rows)
        {
            Assert.Equal("up to date", row.Action);
            Assert.False(string.IsNullOrWhiteSpace(row.CsprojPath));
            Assert.True(string.IsNullOrWhiteSpace(row.NugetName));
        }
    }

    [Fact]
    public void DotnetListPackageRunner_DiscoverTargets_ReturnsCsprojList()
    {
        var repoRoot = GetRepoRoot();

        var targets = DotnetListPackageRunner.DiscoverTargets(repoRoot);

        Assert.Contains(targets, path => path.EndsWith("NugetSync.Cli.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(targets, path => path.EndsWith("NugetSync.Cli.Tests.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(targets, path => path.EndsWith("SampleApp.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(targets, path => path.EndsWith("SampleApp2.csproj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CliRunner_InitAndRun_UsesSampleProjects()
    {
        var repoRoot = GetRepoRoot();
        var rulesPath = Path.Combine(repoRoot, "samples", "TestRules", "nugetsyncrules.json");
        var tempDataRoot = Path.Combine(repoRoot, "tests", ".temp", "data-root-" + Guid.NewGuid().ToString("N"));
        var reportPath = Path.Combine(tempDataRoot, "out", "NugetSync.Report.tsv");
        var inventoryPath = Path.Combine(tempDataRoot, "out", "NugetSync.Inventory.json");

        using var settingsScope = new SettingsFileScope();
        Directory.CreateDirectory(tempDataRoot);

        var initExit = await CliRunner.RunAsync(new[] { "init", "--data-root", tempDataRoot });
        Assert.Equal(0, initExit);

        var exit = await CliRunner.RunAsync(new[]
        {
            "--repo", repoRoot,
            "--rules", rulesPath,
            "--output", reportPath,
            "--inventory", inventoryPath,
            "--include-transitive", "false"
        });

        Assert.Equal(0, exit);
        Assert.True(File.Exists(reportPath));
        Assert.True(File.Exists(inventoryPath));

        var reportText = File.ReadAllText(reportPath);
        Assert.Contains("Frameworks", reportText);
        Assert.Contains("Newtonsoft.Json", reportText);
        Assert.Contains("Serilog", reportText);
        Assert.Contains("Dapper", reportText);
    }

    [Fact]
    public void RulesWizard_AddRuleInteractive_WritesRulesFile()
    {
        var repoRoot = GetRepoRoot();
        var dataRoot = Path.Combine(repoRoot, "tests", ".temp", "wizard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);

        var input = string.Join(Environment.NewLine, new[]
        {
            "Test.Package",
            "1",
            "2",
            "1.2.3",
            "1",
            "1.*",
            "Handle breaking change.",
            "2"
        }) + Environment.NewLine;

        var originalIn = Console.In;
        try
        {
            Console.SetIn(new StringReader(input));
            RulesWizard.AddRuleInteractive(dataRoot);
        }
        finally
        {
            Console.SetIn(originalIn);
        }

        var rulesPath = Path.Combine(dataRoot, "nugetsyncrules.json");
        Assert.True(File.Exists(rulesPath));

        var rules = RuleEngine.LoadRules(rulesPath);
        var rule = rules.Packages.Single(p => p.Id == "Test.Package");
        Assert.Equal("upgrade", rule.Action);
        Assert.Equal("exact_or_higher", rule.TargetPolicy);
        Assert.Equal("1.2.3", rule.TargetVersion);
        Assert.Single(rule.Upgrades);
        Assert.Equal("1.*", rule.Upgrades[0].From);
        Assert.Equal("Handle breaking change.", rule.Upgrades[0].Notes);
    }

    [Fact]
    public void SettingsStore_SaveAndLoad_RoundTrips()
    {
        using var settingsScope = new SettingsFileScope();
        var tempDataRoot = Path.Combine(GetRepoRoot(), "tests", ".temp", "settings-" + Guid.NewGuid().ToString("N"));
        SettingsStore.Save(new Settings { DataRoot = tempDataRoot });

        var settings = SettingsStore.LoadOrThrow();

        Assert.Equal(tempDataRoot, settings.DataRoot);
    }

    private static string GetRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var sln = Path.Combine(current, "NugetSync.sln");
            if (File.Exists(sln))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Repo root not found.");
    }

    private static async Task<RepoInventory> BuildInventoryFromSamples(string repoRoot)
    {
        var inventory = new RepoInventory
        {
            RepoRoot = repoRoot,
            ProjectUrl = string.Empty,
            RepoRef = string.Empty,
            GeneratedAtUtc = DateTime.UtcNow,
            Projects = new List<ProjectInventory>()
        };

        var sampleProjects = new[]
        {
            Path.Combine(repoRoot, "samples", "SampleApp", "SampleApp.csproj"),
            Path.Combine(repoRoot, "samples", "SampleApp2", "SampleApp2.csproj")
        };

        foreach (var project in sampleProjects)
        {
            await DotnetListPackageRunner.RestoreAsync(repoRoot, project);
            var json = await DotnetListPackageRunner.ListPackagesJsonAsync(repoRoot, project, includeTransitive: false);
            var projects = DotnetListPackageParser.Parse(json, repoRoot);
            inventory.Projects.AddRange(projects);
        }

        return inventory;
    }

    private sealed class SettingsFileScope : IDisposable
    {
        private readonly string _settingsPath;
        private readonly string? _backupContents;

        public SettingsFileScope()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _settingsPath = Path.Combine(appData, "NugetSync", "settings.json");
            _backupContents = File.Exists(_settingsPath) ? File.ReadAllText(_settingsPath) : null;
        }

        public void Dispose()
        {
            try
            {
                if (_backupContents is null)
                {
                    if (File.Exists(_settingsPath))
                    {
                        File.Delete(_settingsPath);
                    }
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                    File.WriteAllText(_settingsPath, _backupContents);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}