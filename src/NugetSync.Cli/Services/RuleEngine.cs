using System.Text.Json;
using NuGet.Versioning;
using NugetSync.Cli.Models;

namespace NugetSync.Cli.Services;

public static class RuleEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static RulesFile LoadRules(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Rules file not found: {path}");
        }

        var json = File.ReadAllText(path);
        var rules = JsonSerializer.Deserialize<RulesFile>(json, JsonOptions);
        if (rules is null)
        {
            throw new InvalidOperationException("Rules file is invalid.");
        }

        return rules;
    }

    public static IReadOnlyList<ReportRow> BuildReportRows(RepoInventory inventory, RulesFile rules)
    {
        var rows = new List<ReportRow>();
        var ruleMap = rules.Packages.ToDictionary(
            rule => rule.Id,
            rule => rule,
            StringComparer.OrdinalIgnoreCase);

        foreach (var project in inventory.Projects)
        {
            var aggregated = AggregatePackages(project);
            var projectHadAction = false;
            foreach (var package in aggregated.Values)
            {
                if (!ruleMap.TryGetValue(package.Id, out var rule))
                {
                    continue;
                }

                var includeTransitive = rule.IncludeTransitive ?? rules.DefaultIncludeTransitive;
                if (package.IsTransitive && !includeTransitive)
                {
                    continue;
                }

                var action = rule.Action?.Trim().ToLowerInvariant() ?? "upgrade";
                var currentVersion = package.ResolvedVersion;

                if (action == "remove")
                {
                    rows.Add(new ReportRow
                    {
                        ProjectUrl = inventory.ProjectUrl,
                        RepoRef = inventory.RepoRef,
                        CsprojPath = project.CsprojPath,
                        Frameworks = FormatFrameworks(package.Frameworks),
                        NugetName = package.Id,
                        IsTransitive = package.IsTransitive,
                        Action = "remove",
                        TargetVersion = string.Empty,
                        Comment = SelectComment(rule, currentVersion),
                        DateUpdatedLocal = DateTime.Now
                    });
                    projectHadAction = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(rule.TargetVersion))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(currentVersion))
                {
                    continue;
                }

                var requiresChange = RequiresAction(currentVersion, rule.TargetVersion, rule.TargetPolicy);
                if (!requiresChange)
                {
                    continue;
                }

                rows.Add(new ReportRow
                {
                    ProjectUrl = inventory.ProjectUrl,
                    RepoRef = inventory.RepoRef,
                    CsprojPath = project.CsprojPath,
                    Frameworks = FormatFrameworks(package.Frameworks),
                    NugetName = package.Id,
                    IsTransitive = package.IsTransitive,
                    Action = "upgrade",
                    TargetVersion = rule.TargetVersion ?? string.Empty,
                    Comment = SelectComment(rule, currentVersion),
                    DateUpdatedLocal = DateTime.Now
                });
                projectHadAction = true;
            }

            if (!projectHadAction)
            {
                rows.Add(new ReportRow
                {
                    ProjectUrl = inventory.ProjectUrl,
                    RepoRef = inventory.RepoRef,
                    CsprojPath = project.CsprojPath,
                    Frameworks = FormatProjectFrameworks(project),
                    NugetName = string.Empty,
                    IsTransitive = false,
                    Action = "up to date",
                    TargetVersion = string.Empty,
                    Comment = string.Empty,
                    DateUpdatedLocal = DateTime.Now
                });
            }
        }

        return rows;
    }

    private static Dictionary<string, AggregatedPackage> AggregatePackages(ProjectInventory project)
    {
        var map = new Dictionary<string, AggregatedPackage>(StringComparer.OrdinalIgnoreCase);

        foreach (var framework in project.Frameworks)
        {
            foreach (var package in framework.Packages)
            {
                if (!map.TryGetValue(package.Id, out var existing))
                {
                    map[package.Id] = new AggregatedPackage
                    {
                        Id = package.Id,
                        ResolvedVersion = package.ResolvedVersion,
                        IsTransitive = package.IsTransitive,
                        Frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    };
                    existing = map[package.Id];
                }

                existing.Frameworks.Add(framework.Tfm);
                if (!package.IsTransitive)
                {
                    existing.IsTransitive = false;
                }

                if (TryCompareVersion(package.ResolvedVersion, existing.ResolvedVersion, out var isHigher) && isHigher)
                {
                    existing.ResolvedVersion = package.ResolvedVersion;
                }
            }
        }

        return map;
    }

    private static string FormatFrameworks(IReadOnlyCollection<string> frameworks)
    {
        return string.Join(',', frameworks.OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
    }

    private static string FormatProjectFrameworks(ProjectInventory project)
    {
        var frameworks = project.Frameworks.Select(f => f.Tfm).Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(',', frameworks.OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
    }

    private sealed class AggregatedPackage
    {
        public string Id { get; init; } = string.Empty;
        public string? ResolvedVersion { get; set; }
        public bool IsTransitive { get; set; } = true;
        public HashSet<string> Frameworks { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static bool RequiresAction(string current, string target, string? policy)
    {
        if (!NuGetVersion.TryParse(current, out var currentVersion) ||
            !NuGetVersion.TryParse(target, out var targetVersion))
        {
            throw new InvalidOperationException($"Invalid version comparison: {current} vs {target}.");
        }

        var normalized = policy?.Trim().ToLowerInvariant() ?? "exact_or_higher";
        return normalized switch
        {
            "higher" => currentVersion <= targetVersion,
            "exact_or_higher" => currentVersion < targetVersion,
            "exact_or_lower" => currentVersion > targetVersion,
            "lower" => currentVersion >= targetVersion,
            "exact" => currentVersion != targetVersion,
            "none" => false,
            _ => currentVersion < targetVersion
        };
    }

    private static string SelectComment(PackageRule rule, string? currentVersion)
    {
        if (string.IsNullOrWhiteSpace(currentVersion))
        {
            return string.Empty;
        }

        foreach (var upgrade in rule.Upgrades)
        {
            if (MatchesFrom(upgrade.From, currentVersion))
            {
                return upgrade.Notes ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static bool MatchesFrom(string? from, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(from))
        {
            return false;
        }

        if (from.Trim() == "*")
        {
            return true;
        }

        if (IsRangeExpression(from))
        {
            if (VersionRange.TryParse(from, out var range) &&
                NuGetVersion.TryParse(currentVersion, out var currentForRange))
            {
                return range.Satisfies(currentForRange);
            }

            return false;
        }

        if (from.Contains('*'))
        {
            return MatchesWildcard(from, currentVersion);
        }

        if (NuGetVersion.TryParse(from, out var exact) &&
            NuGetVersion.TryParse(currentVersion, out var currentForExact))
        {
            return exact == currentForExact;
        }

        return string.Equals(from, currentVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRangeExpression(string value)
    {
        return value.Contains('[') || value.Contains('(') || value.Contains(',');
    }

    private static bool MatchesWildcard(string pattern, string currentVersion)
    {
        var parts = pattern.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var numeric = new List<int>();
        foreach (var part in parts)
        {
            if (part == "*")
            {
                break;
            }

            if (!int.TryParse(part, out var number))
            {
                return false;
            }

            numeric.Add(number);
        }

        if (numeric.Count == 0)
        {
            return false;
        }

        var lower = BuildVersion(numeric, padTo: 3);
        var upper = IncrementVersion(numeric, padTo: 3);
        if (!NuGetVersion.TryParse(lower, out var lowerVersion) ||
            !NuGetVersion.TryParse(upper, out var upperVersion) ||
            !NuGetVersion.TryParse(currentVersion, out var current))
        {
            return false;
        }

        return current >= lowerVersion && current < upperVersion;
    }

    private static string BuildVersion(IReadOnlyList<int> segments, int padTo)
    {
        var list = new List<int>(segments);
        while (list.Count < padTo)
        {
            list.Add(0);
        }

        return string.Join('.', list);
    }

    private static string IncrementVersion(IReadOnlyList<int> segments, int padTo)
    {
        var list = new List<int>(segments);
        if (list.Count == 0)
        {
            list.Add(1);
        }
        else
        {
            var lastIndex = list.Count - 1;
            list[lastIndex] += 1;
        }

        while (list.Count < padTo)
        {
            list.Add(0);
        }

        return string.Join('.', list);
    }

    private static bool TryCompareVersion(string? candidate, string? existing, out bool isHigher)
    {
        isHigher = false;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            isHigher = true;
            return true;
        }

        if (NuGetVersion.TryParse(candidate, out var candidateVersion) &&
            NuGetVersion.TryParse(existing, out var existingVersion))
        {
            isHigher = candidateVersion > existingVersion;
            return true;
        }

        return false;
    }
}
