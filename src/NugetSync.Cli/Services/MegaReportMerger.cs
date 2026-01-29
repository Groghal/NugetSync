using System.Text;

namespace NugetSync.Cli.Services;

public static class MegaReportMerger
{
    public static void MergeReports(string outputsRoot, string outputPath)
    {
        var reportFiles = Directory.EnumerateFiles(outputsRoot, "NugetSync.Report.tsv", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (reportFiles.Count == 0)
        {
            throw new InvalidOperationException("No report files found to merge.");
        }

        var headerWritten = false;
        var sb = new StringBuilder();

        foreach (var report in reportFiles)
        {
            using var reader = new StreamReader(report);
            string? line;
            var isFirstLine = true;
            while ((line = reader.ReadLine()) != null)
            {
                if (isFirstLine)
                {
                    isFirstLine = false;
                    if (!headerWritten)
                    {
                        sb.AppendLine(line);
                        headerWritten = true;
                    }

                    continue;
                }

                if (line.Length == 0)
                {
                    continue;
                }

                sb.AppendLine(line);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }
}
