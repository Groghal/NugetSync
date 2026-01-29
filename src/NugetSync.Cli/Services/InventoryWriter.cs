using System.Text.Json;
using NugetSync.Cli.Models;

namespace NugetSync.Cli.Services;

public static class InventoryWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static void Write(string path, RepoInventory inventory)
    {
        var json = JsonSerializer.Serialize(inventory, JsonOptions);
        File.WriteAllText(path, json);
    }
}
