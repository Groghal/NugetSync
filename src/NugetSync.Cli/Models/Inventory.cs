namespace NugetSync.Cli.Models;

public sealed class RepoInventory
{
    public string RepoRoot { get; set; } = string.Empty;
    public string ProjectUrl { get; set; } = string.Empty;
    public string RepoRef { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public string CommitSha { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public List<ProjectInventory> Projects { get; set; } = new();
}

public sealed class ProjectInventory
{
    public string CsprojPath { get; set; } = string.Empty;
    public List<FrameworkInventory> Frameworks { get; set; } = new();
}

public sealed class FrameworkInventory
{
    public string Tfm { get; set; } = string.Empty;
    public List<PackageInventory> Packages { get; set; } = new();
}

public sealed class PackageInventory
{
    public string Id { get; set; } = string.Empty;
    public string? RequestedVersion { get; set; }
    public string? ResolvedVersion { get; set; }
    public bool IsTransitive { get; set; }
}
