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
        sb.AppendLine("ProjectUrl\tRepoRef\tCsprojPath\tFrameworks\tNugetName\tAction\tTargetVersion\tComment\tDateUpdated");

        foreach (var row in rows)
        {
            sb.AppendLine(string.Join('\t', new[]
            {
                Clean(row.ProjectUrl),
                Clean(row.RepoRef),
                Clean(row.CsprojPath),
                Clean(row.Frameworks),
                Clean(row.NugetName),
                Clean(row.Action),
                Quote(Clean(row.TargetVersion)),
                Clean(row.Comment),
                FormatDate(row.DateUpdatedLocal)
            }));
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
