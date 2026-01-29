namespace NugetSync.Cli.Models;

public sealed class RulesFile
{
    public int SchemaVersion { get; set; } = 1;
    public bool DefaultIncludeTransitive { get; set; } = false;
    public List<PackageRule> Packages { get; set; } = new();
}

public sealed class PackageRule
{
    public string Id { get; set; } = string.Empty;
    public string Action { get; set; } = "upgrade";
    public string? TargetVersion { get; set; }
    public string TargetPolicy { get; set; } = "exact_or_higher";
    public bool? IncludeTransitive { get; set; }
    public List<UpgradeRule> Upgrades { get; set; } = new();
}

public sealed class UpgradeRule
{
    public string From { get; set; } = "*";
    public string? To { get; set; }
    public string Notes { get; set; } = string.Empty;
}
