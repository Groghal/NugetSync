using System.Text.Json;
using NugetSync.Cli.Models;

namespace NugetSync.Cli.Services;

public static class DotnetListPackageParser
{
    public static List<ProjectInventory> Parse(string json, string repoRoot)
    {
        using var doc = JsonDocument.Parse(json);
        var projects = new List<ProjectInventory>();

        if (!doc.RootElement.TryGetProperty("projects", out var projectsElement))
        {
            return projects;
        }

        foreach (var projectElement in projectsElement.EnumerateArray())
        {
            if (!projectElement.TryGetProperty("path", out var pathElement))
            {
                continue;
            }

            var path = pathElement.GetString() ?? string.Empty;
            var project = new ProjectInventory
            {
                CsprojPath = PathHelpers.ToRepoRelativePath(repoRoot, path)
            };

            if (!projectElement.TryGetProperty("frameworks", out var frameworksElement))
            {
                projects.Add(project);
                continue;
            }

            foreach (var frameworkElement in frameworksElement.EnumerateArray())
            {
                var tfm = frameworkElement.GetProperty("framework").GetString() ?? string.Empty;
                var framework = new FrameworkInventory { Tfm = tfm };

                if (frameworkElement.TryGetProperty("topLevelPackages", out var topLevel))
                {
                    foreach (var pkg in topLevel.EnumerateArray())
                    {
                        framework.Packages.Add(ParsePackage(pkg, isTransitive: false));
                    }
                }

                if (frameworkElement.TryGetProperty("transitivePackages", out var transitive))
                {
                    foreach (var pkg in transitive.EnumerateArray())
                    {
                        framework.Packages.Add(ParsePackage(pkg, isTransitive: true));
                    }
                }

                project.Frameworks.Add(framework);
            }

            projects.Add(project);
        }

        return projects;
    }

    private static PackageInventory ParsePackage(JsonElement pkg, bool isTransitive)
    {
        var id = pkg.GetProperty("id").GetString() ?? string.Empty;
        string? requested = null;
        string? resolved = null;

        if (pkg.TryGetProperty("requestedVersion", out var requestedElement))
        {
            requested = requestedElement.GetString();
        }

        if (pkg.TryGetProperty("resolvedVersion", out var resolvedElement))
        {
            resolved = resolvedElement.GetString();
        }

        return new PackageInventory
        {
            Id = id,
            RequestedVersion = requested,
            ResolvedVersion = resolved,
            IsTransitive = isTransitive
        };
    }
}
