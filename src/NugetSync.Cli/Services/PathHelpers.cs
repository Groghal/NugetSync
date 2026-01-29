using System.Security.Cryptography;
using System.Text;

namespace NugetSync.Cli.Services;

public static class PathHelpers
{
    public static string GetRepoKey(string repoRoot)
    {
        var normalizedRoot = NormalizePath(repoRoot);
        var name = new DirectoryInfo(normalizedRoot).Name;
        var hash = ComputeHash(normalizedRoot);
        return $"{Sanitize(name)}_{hash}";
    }

    public static string ToRepoRelativePath(string repoRoot, string path)
    {
        var relative = Path.GetRelativePath(repoRoot, path);
        return relative.Replace('\\', '/');
    }

    private static string ComputeHash(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }

    private static string NormalizePath(string input)
    {
        return input.Trim().ToLowerInvariant();
    }

    private static string Sanitize(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append('_');
            }
        }

        return sb.ToString();
    }
}
