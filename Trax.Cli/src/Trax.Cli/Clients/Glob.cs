using System.Text;
using System.Text.RegularExpressions;

namespace Trax.Cli.Clients;

/// <summary>
/// The files a <c>--documents</c> glob names: <c>*</c> any characters but <c>/</c>, <c>?</c> one of them, <c>**</c> any
/// folders. A linked folder is not entered. Paths use <c>/</c> on every platform.
/// </summary>
internal static class Glob
{
    /// <summary>Every file under <paramref name="root"/> that <paramref name="pattern"/> matches, as full paths.</summary>
    public static IReadOnlyList<string> Files(string root, string pattern)
    {
        var (baseDir, rest) = Split(Path.GetFullPath(root), pattern);
        if (!Directory.Exists(baseDir))
            return File.Exists(baseDir) && rest.Length == 0 ? [baseDir] : [];
        if (rest.Length == 0)
            return [];
        var regex = new Regex("^" + ToRegex(rest) + "$", RegexOptions.CultureInvariant);
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseSensitive,
        };
        return Directory
            .EnumerateFiles(baseDir, "*", enumeration)
            .Where(f => regex.IsMatch(Path.GetRelativePath(baseDir, f).Replace('\\', '/')))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary><paramref name="pattern"/>, relative to <paramref name="cwd"/>, rewritten relative to <paramref name="dir"/>.</summary>
    public static string RelativeTo(string cwd, string dir, string pattern)
    {
        var (baseDir, rest) = Split(Path.GetFullPath(cwd), pattern);
        var relative = Path.GetRelativePath(Path.GetFullPath(dir), baseDir).Replace('\\', '/');
        return rest.Length == 0 ? relative
            : relative == "." ? rest
            : $"{relative}/{rest}";
    }

    // The folder before the first segment holding a wildcard, and the pattern from there.
    private static (string Base, string Remainder) Split(string root, string pattern)
    {
        var segments = pattern.Replace('\\', '/').Split('/');
        var at = Array.FindIndex(segments, s => s.IndexOfAny(['*', '?']) >= 0);
        var fixedPart = at < 0 ? segments : segments[..at];
        var baseDir = Path.GetFullPath(Path.Combine([root, .. fixedPart]));
        return (baseDir, at < 0 ? "" : string.Join('/', segments[at..]));
    }

    private static string ToRegex(string pattern)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                // `**/` is any folders, none included; a trailing `**` is anything.
                if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                {
                    sb.Append("(?:.*/)?");
                    i += 2;
                }
                else
                {
                    sb.Append(".*");
                    i++;
                }
            }
            else if (c == '*')
                sb.Append("[^/]*");
            else if (c == '?')
                sb.Append("[^/]");
            else
                sb.Append(Regex.Escape(c.ToString()));
        }
        return sb.ToString();
    }
}
