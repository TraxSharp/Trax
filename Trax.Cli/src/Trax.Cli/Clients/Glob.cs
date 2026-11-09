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
        var regex = new Regex(@"\A" + ToRegex(rest) + @"\z", RegexOptions.CultureInvariant);
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

    /// <summary>
    /// <paramref name="pattern"/>, relative to <paramref name="cwd"/>, rewritten relative to <paramref name="dir"/>.
    /// Both sides are compared with their links resolved: the working directory is always reported that way
    /// (<c>/private/var/...</c> on macOS, where <c>/var</c> is a link), and a folder named through a link would
    /// otherwise look like it sits elsewhere, giving a path that climbs to the root and back.
    /// </summary>
    public static string RelativeTo(string cwd, string dir, string pattern)
    {
        var (baseDir, rest) = Split(Path.GetFullPath(cwd), pattern);
        var relative = Path.GetRelativePath(Resolved(dir), Resolved(baseDir)).Replace('\\', '/');
        return rest.Length == 0 ? relative
            : relative == "." ? rest
            : $"{relative}/{rest}";
    }

    /// <summary>
    /// The full path with every link on the way to it resolved, as far as it exists; the rest is appended as written.
    /// Two paths name the same place when these are equal, whichever links each was given through.
    /// </summary>
    public static string Resolved(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var resolved = Path.GetPathRoot(full)!;
        var parts = full[resolved.Length..]
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var next = Path.Combine(resolved, parts[i]);
            var info = new DirectoryInfo(next);
            if (!info.Exists)
                return Path.Combine([next, .. parts[(i + 1)..]]);
            resolved =
                info.LinkTarget is null ? next
                : info.ResolveLinkTarget(returnFinalTarget: true) is { } target
                    ? Resolved(target.FullName)
                : next;
        }
        return resolved;
    }

    // The folder before the first segment holding a wildcard, and the pattern from there. The folder's part of the
    // pattern is combined with the root as one path, so one that is rooted (/home/..., C:/...) is read from where it
    // points rather than as folders under the root.
    private static (string Base, string Remainder) Split(string root, string pattern)
    {
        var segments = pattern.Replace('\\', '/').Split('/');
        var at = Array.FindIndex(segments, s => s.IndexOfAny(['*', '?']) >= 0);
        var fixedPart =
            at < 0 ? string.Join('/', segments)
            : at == 0 ? ""
            : string.Join('/', segments[..at]) + '/';
        var baseDir = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.Combine(root, fixedPart))
        );
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
