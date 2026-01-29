using System.Diagnostics;
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
            Assert.Equal("ProjectUrl\tRepoRef\tCsprojPath\tFrameworks\tNugetName\tIsTransitive\tAction\tTargetVersion\tComment\tDateUpdated", header);
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
    public void RuleEngine_DefaultIncludeTransitive_SkipsTransitivePackages()
    {
        var inventory = BuildInventoryWithDirectAndTransitive();
        var rules = new RulesFile
        {
            DefaultIncludeTransitive = false,
            Packages = new List<PackageRule>
            {
                new() { Id = "DirectOnly", Action = "upgrade", TargetVersion = "2.0.0", TargetPolicy = "exact_or_higher" },
                new() { Id = "TransitiveOnly", Action = "upgrade", TargetVersion = "2.0.0", TargetPolicy = "exact_or_higher" }
            }
        };

        var rows = RuleEngine.BuildReportRows(inventory, rules);

        var directRow = rows.FirstOrDefault(r => r.NugetName == "DirectOnly");
        var transitiveRow = rows.FirstOrDefault(r => r.NugetName == "TransitiveOnly");
        Assert.NotNull(directRow);
        Assert.Null(transitiveRow);
        Assert.False(directRow.IsTransitive);
    }

    [Fact]
    public void RuleEngine_IncludeTransitiveTrue_MatchesTransitivePackages()
    {
        var inventory = BuildInventoryWithDirectAndTransitive();
        var rules = new RulesFile
        {
            DefaultIncludeTransitive = false,
            Packages = new List<PackageRule>
            {
                new() { Id = "DirectOnly", Action = "upgrade", TargetVersion = "2.0.0", TargetPolicy = "exact_or_higher" },
                new() { Id = "TransitiveOnly", Action = "upgrade", TargetVersion = "2.0.0", TargetPolicy = "exact_or_higher", IncludeTransitive = true }
            }
        };

        var rows = RuleEngine.BuildReportRows(inventory, rules);

        var directRow = rows.FirstOrDefault(r => r.NugetName == "DirectOnly");
        var transitiveRow = rows.FirstOrDefault(r => r.NugetName == "TransitiveOnly");
        Assert.NotNull(directRow);
        Assert.NotNull(transitiveRow);
        Assert.False(directRow.IsTransitive);
        Assert.True(transitiveRow.IsTransitive);
    }

    [Fact]
    public void RuleEngine_IncludeTransitiveNull_UsesDefaultIncludeTransitive()
    {
        var inventory = BuildInventoryWithDirectAndTransitive();
        var rules = new RulesFile
        {
            DefaultIncludeTransitive = true,
            Packages = new List<PackageRule>
            {
                new() { Id = "TransitiveOnly", Action = "upgrade", TargetVersion = "2.0.0", TargetPolicy = "exact_or_higher", IncludeTransitive = null }
            }
        };

        var rows = RuleEngine.BuildReportRows(inventory, rules);

        var transitiveRow = rows.FirstOrDefault(r => r.NugetName == "TransitiveOnly");
        Assert.NotNull(transitiveRow);
        Assert.True(transitiveRow.IsTransitive);
    }

    private static RepoInventory BuildInventoryWithDirectAndTransitive()
    {
        return new RepoInventory
        {
            ProjectUrl = "https://test",
            RepoRef = "main",
            Projects = new List<ProjectInventory>
            {
                new()
                {
                    CsprojPath = "test/test.csproj",
                    Frameworks = new List<FrameworkInventory>
                    {
                        new()
                        {
                            Tfm = "net8.0",
                            Packages = new List<PackageInventory>
                            {
                                new() { Id = "DirectOnly", ResolvedVersion = "1.0.0", IsTransitive = false },
                                new() { Id = "TransitiveOnly", ResolvedVersion = "1.0.0", IsTransitive = true }
                            }
                        }
                    }
                }
            }
        };
    }

    [Fact]
    public void MegaReportMerger_SkipsHeadersAndKeepsRows()
    {
        var root = GetRepoRoot();
        var tempDir = Path.Combine(root, "tests", ".temp", "merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var report1 = Path.Combine(tempDir, "repo1", "NugetSync.Report.tsv");
        var report2 = Path.Combine(tempDir, "repo2", "NugetSync.Report.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(report1)!);
        Directory.CreateDirectory(Path.GetDirectoryName(report2)!);

        File.WriteAllText(report1, string.Join(Environment.NewLine, new[]
        {
            "ProjectUrl\tRepoRef\tCsprojPath\tFrameworks\tNugetName\tIsTransitive\tAction\tTargetVersion\tComment\tDateUpdated",
            "p1\tr1\tc1\tf1\tPkgA\tfalse\tupgrade\t'1.0.0'\tNote A\t2026-01-29 12:00:00",
            ""
        }));

        File.WriteAllText(report2, string.Join(Environment.NewLine, new[]
        {
            "ProjectUrl\tRepoRef\tCsprojPath\tFrameworks\tNugetName\tIsTransitive\tAction\tTargetVersion\tComment\tDateUpdated",
            "p2\tr2\tc2\tf2\tPkgB\tfalse\tremove\t''\tNote B\t2026-01-29 12:01:00",
            ""
        }));

        var outputPath = Path.Combine(tempDir, "NugetSync.MegaReport.tsv");
        MegaReportMerger.MergeReports(tempDir, outputPath);

        var lines = File.ReadAllLines(outputPath);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("ProjectUrl\tRepoRef", lines[0]);
        Assert.Contains("PkgA", lines[1]);
        Assert.Contains("PkgB", lines[2]);
    }

    [Fact]
    public async Task CliRunner_Interactive_PastesDirectoriesAndRunsMerge()
    {
        var repoRoot = GetRepoRoot();
        var tempDataRoot = Path.Combine(repoRoot, "tests", ".temp", "interactive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDataRoot);
        File.WriteAllText(Path.Combine(tempDataRoot, "nugetsyncrules.json"), "{\"schemaVersion\":1,\"packages\":[]}");

        var input = string.Join(Environment.NewLine, new[]
        {
            Path.Combine(repoRoot, "samples", "SampleApp"),
            Path.Combine(repoRoot, "samples", "SampleApp2"),
            Path.Combine(repoRoot, "samples", "SampleApp"),
            "done"
        }) + Environment.NewLine;

        using var settingsScope = new SettingsFileScope();
        SettingsStore.Save(new Settings { DataRoot = tempDataRoot });

        var originalIn = Console.In;
        try
        {
            Console.SetIn(new StringReader(input));
            var exit = await CliRunner.RunAsync(new[] { "interactive" });
            Assert.Equal(0, exit);
        }
        finally
        {
            Console.SetIn(originalIn);
        }

        var outputsRoot = Path.Combine(tempDataRoot, "outputs");
        var megaPath = Path.Combine(outputsRoot, "NugetSync.MegaReport.tsv");
        Assert.True(File.Exists(megaPath));
        Assert.True(Directory.EnumerateFiles(outputsRoot, "NugetSync.Report.tsv", SearchOption.AllDirectories).Any());
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
            "run",
            "--repo", repoRoot,
            "--rules", rulesPath,
            "--output", reportPath,
            "--inventory", inventoryPath,
            "--include-transitive", "false",
            "--force"
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
    public async Task CliRunner_SkipsWhenBranchAndCommitMatch()
    {
        var repoRoot = GetRepoRoot();
        var dataRoot = Path.Combine(repoRoot, "tests", ".temp", "skip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, "nugetsyncrules.json"), "{\"schemaVersion\":1,\"packages\":[]}");

        using var settingsScope = new SettingsFileScope();
        SettingsStore.Save(new Settings { DataRoot = dataRoot });

        var repoKey = PathHelpers.GetRepoKey(repoRoot);
        var outputsDir = Path.Combine(dataRoot, "outputs", repoKey);
        Directory.CreateDirectory(outputsDir);
        var inventoryPath = Path.Combine(outputsDir, "NugetSync.Inventory.json");
        var reportPath = Path.Combine(outputsDir, "NugetSync.Report.tsv");

        var branch = GitInfoProvider.GetRepoRef(repoRoot) ?? string.Empty;
        var sha = GitInfoProvider.GetCommitSha(repoRoot) ?? string.Empty;

        var inventory = new RepoInventory
        {
            RepoRoot = repoRoot,
            BranchName = branch,
            CommitSha = sha,
            GeneratedAtUtc = DateTime.UtcNow
        };

        InventoryWriter.Write(inventoryPath, inventory);

        var exit = await CliRunner.RunAsync(new[] { "run", "--repo", repoRoot, "--output", reportPath, "--inventory", inventoryPath });
        Assert.Equal(0, exit);
        Assert.False(File.Exists(reportPath));
    }

    [Fact]
    public async Task CliRunner_ForceBypassesSkip()
    {
        var repoRoot = GetRepoRoot();
        var dataRoot = Path.Combine(repoRoot, "tests", ".temp", "force-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(Path.Combine(dataRoot, "nugetsyncrules.json"), "{\"schemaVersion\":1,\"packages\":[]}");

        using var settingsScope = new SettingsFileScope();
        SettingsStore.Save(new Settings { DataRoot = dataRoot });

        var repoKey = PathHelpers.GetRepoKey(repoRoot);
        var outputsDir = Path.Combine(dataRoot, "outputs", repoKey);
        Directory.CreateDirectory(outputsDir);
        var inventoryPath = Path.Combine(outputsDir, "NugetSync.Inventory.json");
        var reportPath = Path.Combine(outputsDir, "NugetSync.Report.tsv");

        var branch = GitInfoProvider.GetRepoRef(repoRoot) ?? string.Empty;
        var sha = GitInfoProvider.GetCommitSha(repoRoot) ?? string.Empty;

        var inventory = new RepoInventory
        {
            RepoRoot = repoRoot,
            BranchName = branch,
            CommitSha = sha,
            GeneratedAtUtc = DateTime.UtcNow
        };

        InventoryWriter.Write(inventoryPath, inventory);

        var exit = await CliRunner.RunAsync(new[] { "run", "--repo", repoRoot, "--output", reportPath, "--inventory", inventoryPath, "--force" });
        Assert.Equal(0, exit);
        Assert.True(File.Exists(reportPath));
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
    public void RulesWizard_AddRulesMassInteractive_WritesAllPackages()
    {
        var repoRoot = GetRepoRoot();
        var dataRoot = Path.Combine(repoRoot, "tests", ".temp", "wizard-mass-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);

        var input = string.Join(Environment.NewLine, new[]
        {
            "1",
            "2",
            "1.2.3",
            "1",
            "1.*",
            "Shared note",
            "2",
            "Package.A",
            "Package.B",
            "Package.A",
            "done"
        }) + Environment.NewLine;

        var originalIn = Console.In;
        try
        {
            Console.SetIn(new StringReader(input));
            RulesWizard.AddRulesMassInteractive(dataRoot);
        }
        finally
        {
            Console.SetIn(originalIn);
        }

        var rulesPath = Path.Combine(dataRoot, "nugetsyncrules.json");
        Assert.True(File.Exists(rulesPath));

        var rules = RuleEngine.LoadRules(rulesPath);
        Assert.Equal(2, rules.Packages.Count);
        Assert.Contains(rules.Packages, p => p.Id == "Package.A");
        Assert.Contains(rules.Packages, p => p.Id == "Package.B");
        Assert.All(rules.Packages, p =>
        {
            Assert.Equal("upgrade", p.Action);
            Assert.Equal("exact_or_higher", p.TargetPolicy);
            Assert.Equal("1.2.3", p.TargetVersion);
            Assert.Single(p.Upgrades);
            Assert.Equal("Shared note", p.Upgrades[0].Notes);
        });
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

    [Fact]
    public void GitInfoProvider_HasUncommittedChanges_ReturnsFalseWhenClean()
    {
        var repoRoot = CreateTempGitRepo(commit: true, extraFile: false);
        try
        {
            var hasChanges = GitInfoProvider.HasUncommittedChanges(repoRoot);
            Assert.False(hasChanges);
        }
        finally
        {
            TryDeleteDir(repoRoot);
        }
    }

    [Fact]
    public void GitInfoProvider_HasUncommittedChanges_ReturnsTrueWhenDirty()
    {
        var repoRoot = CreateTempGitRepo(commit: true, extraFile: true);
        try
        {
            var hasChanges = GitInfoProvider.HasUncommittedChanges(repoRoot);
            Assert.True(hasChanges);
        }
        finally
        {
            TryDeleteDir(repoRoot);
        }
    }

    [Fact]
    public void GitInfoProvider_Checkout_SwitchesBranch()
    {
        var repoRoot = CreateTempGitRepoWithBranches();
        try
        {
            RunGit(repoRoot, "checkout main");
            Assert.Equal("main", GitInfoProvider.GetRepoRef(repoRoot));

            var (success, error) = GitInfoProvider.Checkout(repoRoot, "develop");
            Assert.True(success, error ?? "checkout failed");
            Assert.Equal("develop", GitInfoProvider.GetRepoRef(repoRoot));
        }
        finally
        {
            TryDeleteDir(repoRoot);
        }
    }

    [Fact]
    public void GitInfoProvider_Pull_ReturnsErrorWhenNoRemote()
    {
        var repoRoot = CreateTempGitRepo(commit: true, extraFile: false);
        try
        {
            var (success, error) = GitInfoProvider.Pull(repoRoot);
            Assert.False(success);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
        finally
        {
            TryDeleteDir(repoRoot);
        }
    }

    [Fact]
    public async Task CliRunner_Update_ExitsWithErrorWhenNoRepos()
    {
        var repoRoot = GetRepoRoot();
        var tempDataRoot = Path.Combine(repoRoot, "tests", ".temp", "update-none-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDataRoot);

        using var settingsScope = new SettingsFileScope();
        SettingsStore.Save(new Settings { DataRoot = tempDataRoot });

        var exit = await CliRunner.RunAsync(new[] { "update" });
        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task CliRunner_Update_SkipsDirtyRepoAndReportsNoFailure()
    {
        var repoRoot = GetRepoRoot();
        var dirtyRepo = CreateTempGitRepo(commit: true, extraFile: true);
        var tempDataRoot = Path.Combine(repoRoot, "tests", ".temp", "update-dirty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDataRoot);
        File.WriteAllText(Path.Combine(tempDataRoot, "nugetsyncrules.json"), "{\"schemaVersion\":1,\"packages\":[]}");

        var repoKey = PathHelpers.GetRepoKey(dirtyRepo);
        var outputsDir = Path.Combine(tempDataRoot, "outputs", repoKey);
        Directory.CreateDirectory(outputsDir);
        var inventoryPath = Path.Combine(outputsDir, "NugetSync.Inventory.json");
        var inventory = new RepoInventory
        {
            RepoRoot = dirtyRepo,
            BranchName = "main",
            CommitSha = RunGit(dirtyRepo, "rev-parse HEAD")?.Trim() ?? "",
            GeneratedAtUtc = DateTime.UtcNow,
            Projects = new List<ProjectInventory>()
        };
        InventoryWriter.Write(inventoryPath, inventory);

        using var settingsScope = new SettingsFileScope();
        SettingsStore.Save(new Settings { DataRoot = tempDataRoot });

        try
        {
            var exit = await CliRunner.RunAsync(new[] { "update", "--branch", "main" });
            Assert.Equal(0, exit);
        }
        finally
        {
            TryDeleteDir(dirtyRepo);
        }
    }

    private static string CreateTempGitRepo(bool commit, bool extraFile)
    {
        var repoRoot = Path.Combine(GetRepoRoot(), "tests", ".temp", "git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repoRoot);
        RunGit(repoRoot, "init");
        RunGit(repoRoot, "config user.email test@test.com");
        RunGit(repoRoot, "config user.name Test");
        File.WriteAllText(Path.Combine(repoRoot, "readme.txt"), "initial");
        RunGit(repoRoot, "add readme.txt");
        if (commit)
        {
            RunGit(repoRoot, "commit -m initial");
        }

        if (extraFile)
        {
            File.WriteAllText(Path.Combine(repoRoot, "extra.txt"), "dirty");
        }

        return repoRoot;
    }

    private static string CreateTempGitRepoWithBranches()
    {
        var repoRoot = CreateTempGitRepo(commit: true, extraFile: false);
        RunGit(repoRoot, "branch -m main");
        RunGit(repoRoot, "checkout -b develop");
        return repoRoot;
    }

    private static string? RunGit(string repoRoot, string args)
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
        if (process == null) return null;
        var stdout = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit(5000);
        return process.ExitCode == 0 ? stdout.Trim() : null;
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
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