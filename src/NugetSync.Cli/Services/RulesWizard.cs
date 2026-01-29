using System.Text.Json;
using NugetSync.Cli.Models;

namespace NugetSync.Cli.Services;

public static class RulesWizard
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static void AddRuleInteractive(string dataRoot)
    {
        var rulesPath = Path.Combine(dataRoot, "nugetsyncrules.json");
        var rules = LoadOrCreate(rulesPath);

        Console.WriteLine("Add package rule");
        var id = Prompt("Package id");
        var action = PromptChoice("Action", new[] { "upgrade", "remove" });

        var rule = rules.Packages.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                   ?? new PackageRule { Id = id };

        rule.Action = action;

        if (action == "upgrade")
        {
            var policy = PromptChoice("Target policy", new[]
            {
                "higher",
                "exact_or_higher",
                "exact",
                "exact_or_lower",
                "lower"
            });
            var targetVersion = Prompt("Target version");
            rule.TargetPolicy = policy;
            rule.TargetVersion = targetVersion;
        }
        else
        {
            rule.TargetPolicy = "none";
            rule.TargetVersion = null;
        }

        rule.Upgrades = new List<UpgradeRule>();
        while (true)
        {
            var addUpgrade = PromptChoice("Add upgrade note?", new[] { "yes", "no" });
            if (addUpgrade == "no")
            {
                break;
            }

            var from = Prompt("From version (range, wildcard, or *)");
            var notes = Prompt("Notes");
            rule.Upgrades.Add(new UpgradeRule
            {
                From = from,
                To = rule.TargetVersion,
                Notes = notes
            });
        }

        var existingIndex = rules.Packages.FindIndex(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            rules.Packages[existingIndex] = rule;
        }
        else
        {
            rules.Packages.Add(rule);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(rulesPath)!);
        File.WriteAllText(rulesPath, JsonSerializer.Serialize(rules, JsonOptions));
        Console.WriteLine($"Rules saved to {rulesPath}");
    }

    private static RulesFile LoadOrCreate(string rulesPath)
    {
        if (!File.Exists(rulesPath))
        {
            return new RulesFile { SchemaVersion = 1 };
        }

        var json = File.ReadAllText(rulesPath);
        return JsonSerializer.Deserialize<RulesFile>(json, JsonOptions) ?? new RulesFile { SchemaVersion = 1 };
    }

    private static string Prompt(string label)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            var input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input))
            {
                return input.Trim();
            }
        }
    }

    private static string PromptChoice(string label, IReadOnlyList<string> options)
    {
        while (true)
        {
            Console.WriteLine($"{label}:");
            for (var i = 0; i < options.Count; i++)
            {
                Console.WriteLine($"  {i + 1}) {options[i]}");
            }

            Console.Write("Select: ");
            var input = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            if (int.TryParse(input, out var index) && index >= 1 && index <= options.Count)
            {
                return options[index - 1];
            }

            var match = options.FirstOrDefault(option => option.Equals(input, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match;
            }
        }
    }
}
