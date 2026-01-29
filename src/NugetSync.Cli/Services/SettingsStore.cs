using System.Text.Json;
using NugetSync.Cli.Models;

namespace NugetSync.Cli.Services;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string GetSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "NugetSync", "settings.json");
    }

    public static Settings LoadOrThrow()
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("Settings not found. Run: NugetSync init --data-root <path>");
            Environment.Exit(2);
        }

        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
        if (settings is null || string.IsNullOrWhiteSpace(settings.DataRoot))
        {
            Console.Error.WriteLine("Settings file is invalid. Run: NugetSync init --data-root <path>");
            Environment.Exit(2);
        }

        var normalized = NormalizeDataRoot(settings.DataRoot);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            Console.Error.WriteLine("Settings file is invalid. Run: NugetSync init --data-root <path>");
            Environment.Exit(2);
        }

        if (!string.Equals(settings.DataRoot, normalized, StringComparison.Ordinal))
        {
            settings.DataRoot = normalized;
            Save(settings);
        }

        return settings;
    }

    public static void Save(Settings settings)
    {
        settings.DataRoot = NormalizeDataRoot(settings.DataRoot);
        if (string.IsNullOrWhiteSpace(settings.DataRoot))
        {
            throw new ArgumentException("DataRoot is required.", nameof(settings));
        }

        var path = GetSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static string NormalizeDataRoot(string? dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            return string.Empty;
        }

        var trimmed = dataRoot.Trim();
        trimmed = trimmed.Replace("\"", string.Empty).Trim();
        trimmed = trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed;
    }
}
