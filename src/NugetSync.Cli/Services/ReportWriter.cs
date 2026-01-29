using System.Text;
using NugetSync.Cli.Models;

namespace NugetSync.Cli.Services;

public sealed class ReportRow
{
    public string ProjectUrl { get; set; } = string.Empty;
    public string RepoRef { get; set; } = string.Empty;
    public string CsprojPath { get; set; } = string.Empty;
    public string Frameworks { get; set; } = string.Empty;
    public string NugetName { get; set; } = string.Empty;
    public bool IsTransitive { get; set; }
    public string Action { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public DateTime DateUpdatedLocal { get; set; }
}

public static class ReportWriter
{
    public static void WriteTsv(string path, IReadOnlyList<ReportRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProjectUrl\tRepoRef\tCsprojPath\tFrameworks\tNugetName\tIsTransitive\tAction\tTargetVersion\tComment\tDateUpdated");

        foreach (var row in rows)
        {
            sb.AppendLine(string.Join('\t', new[]
            {
                Clean(row.ProjectUrl),
                Clean(row.RepoRef),
                Clean(row.CsprojPath),
                Clean(row.Frameworks),
                Clean(row.NugetName),
                row.IsTransitive ? "TRUE" : "FALSE",
                Clean(row.Action),
                Quote(Clean(row.TargetVersion)),
                Clean(row.Comment),
                FormatDate(row.DateUpdatedLocal)
            }));
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    public static void WritePackagesTsv(string path, RepoInventory inventory)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ProjectUrl\tRepoRef\tCsprojPath\tFramework\tPackage\tVersion\tIsTransitive\tDateUpdated");

        var dateUpdated = inventory.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        foreach (var project in inventory.Projects)
        {
            foreach (var framework in project.Frameworks)
            {
                foreach (var pkg in framework.Packages)
                {
                    sb.AppendLine(string.Join('\t', new[]
                    {
                        Clean(inventory.ProjectUrl),
                        Clean(inventory.RepoRef),
                        Clean(project.CsprojPath),
                        Clean(framework.Tfm),
                        Clean(pkg.Id),
                        Clean(pkg.ResolvedVersion ?? ""),
                        pkg.IsTransitive ? "TRUE" : "FALSE",
                        dateUpdated
                    }));
                }
            }
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
    }

    private static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return $"'{value}'";
    }

    private static string FormatDate(DateTime dateTime)
    {
        if (dateTime == default)
        {
            return string.Empty;
        }

        return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
