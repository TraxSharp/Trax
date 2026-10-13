namespace Trax.Docs.Snippets.Tests.Infrastructure;

/// <summary>
/// Checks that a fence marked <c>from=path[#region]</c> is an excerpt of that file.
///
/// <para>
/// The fence is cut into chunks at elision lines: a line that is exactly <c>// ...</c>,
/// <c># ...</c> or <c>&lt;!-- ... --&gt;</c> once trimmed. Each chunk must occur in the region as
/// consecutive lines, the chunks in the order the fence gives them. Both sides are compared with
/// trailing whitespace trimmed and their common indentation removed, so a chunk lifted out of a
/// nested block matches without its outer indentation, and blank lines at either end of a chunk
/// are dropped.
/// </para>
/// <para>
/// <c>#name</c> selects the lines between <c>#region name</c> and its <c>#endregion</c> (C#), or
/// <c>// #region name</c> and <c>// #endregion</c> (TypeScript, JavaScript), or
/// <c>&lt;!-- #region name --&gt;</c> and <c>&lt;!-- #endregion --&gt;</c> (XML, a csproj). Without
/// it the whole file is the region. Region marker lines are left out on both sides, so a fence
/// need not show them and may.
/// </para>
/// </summary>
public static class Excerpts
{
    private static readonly Regex RegionStart = new(
        @"^\s*(?://\s*|<!--\s*)?#region(?:\s+(?<name>.*?))?\s*(?:-->)?\s*$",
        RegexOptions.Compiled
    );

    private static readonly Regex RegionEnd = new(
        @"^\s*(?://\s*|<!--\s*)?#endregion\b.*$",
        RegexOptions.Compiled
    );

    private static readonly HashSet<string> Elisions = new(StringComparer.Ordinal)
    {
        "// ...",
        "# ...",
        "<!-- ... -->",
    };

    /// <summary>
    /// What is wrong with <paramref name="fence"/> as an excerpt of the file it names, or null
    /// when it matches. Reads the file from the repository root.
    /// </summary>
    public static string? Problem(Fence fence)
    {
        var where =
            $"{fence.Location} from={fence.From}"
            + (fence.FromRegion is null ? "" : $"#{fence.FromRegion}");

        if (string.IsNullOrWhiteSpace(fence.From))
            return $"{where}: from= names no file";

        if (
            fence.From.StartsWith('/')
            || fence.From.Contains('\\')
            || fence.From.Split('/').Any(s => s is ".." or ".")
        )
            return $"{where}: the path must be relative to the repository root, forward "
                + "slashes, with no '.' or '..' segment (Trax.Samples/samples/X/Program.cs)";

        var file = Path.Combine(
            new[] { RepoRoot.Monorepo }.Concat(fence.From.Split('/')).ToArray()
        );
        if (!File.Exists(file))
            return $"{where}: no such file under the repository root";

        var problem = Match(fence.Code, File.ReadAllText(file), fence.FromRegion);
        return problem is null ? null : $"{where}: {problem}";
    }

    /// <summary>
    /// What is wrong with <paramref name="code"/> as an excerpt of <paramref name="source"/>, or
    /// of its region <paramref name="region"/>, or null when it matches.
    /// </summary>
    public static string? Match(string code, string source, string? region)
    {
        var lines = Region(source, region, out var regionProblem);
        if (lines is null)
            return regionProblem;

        var target = lines.Select(l => l.TrimEnd()).ToList();
        var chunks = Chunks(code);
        if (chunks.Count == 0)
            return "the fence is empty";

        var from = 0;
        for (var c = 0; c < chunks.Count; c++)
        {
            var (start, chunk) = chunks[c];
            var at = Find(target, chunk, from);
            if (at < 0)
                return $"chunk {c + 1} of {chunks.Count} (fence line {start + 1}) does not occur "
                    + (c == 0 ? "in the file" : "after the previous chunk")
                    + (region is null ? "" : $" region '{region}'")
                    + ". It reads:\n      "
                    + string.Join("\n      ", chunk);
            from = at + chunk.Count;
        }

        return null;
    }

    /// <summary>
    /// The fence's chunks, split at elision lines, each with its first line's 0-based index in
    /// the fence, region marker lines and blank lines at its ends dropped, and its common
    /// indentation removed.
    /// </summary>
    public static List<(int Start, List<string> Lines)> Chunks(string code)
    {
        var chunks = new List<(int, List<string>)>();
        var lines = code.Replace("\r\n", "\n").Split('\n');
        var current = new List<(int Index, string Text)>();

        void Flush()
        {
            while (current.Count > 0 && current[0].Text.Trim().Length == 0)
                current.RemoveAt(0);
            while (current.Count > 0 && current[^1].Text.Trim().Length == 0)
                current.RemoveAt(current.Count - 1);
            if (current.Count > 0)
                chunks.Add((current[0].Index, Dedent(current.Select(l => l.Text).ToList())));
            current = [];
        }

        for (var i = 0; i < lines.Length; i++)
        {
            if (Elisions.Contains(lines[i].Trim()))
                Flush();
            else if (!IsRegionMarker(lines[i]))
                current.Add((i, lines[i]));
        }
        Flush();

        return chunks;
    }

    /// <summary>
    /// The lines of <paramref name="region"/> in <paramref name="source"/>, or of the whole file
    /// when it is null, with region marker lines left out. Null, with the reason, when the
    /// region is not in the file or is never closed.
    /// </summary>
    public static List<string>? Region(string source, string? region, out string? problem)
    {
        problem = null;
        var lines = source.Replace("\r\n", "\n").Split('\n');

        if (region is null)
            return lines.Where(l => !IsRegionMarker(l)).ToList();

        var open = Array.FindIndex(
            lines,
            l => RegionStart.Match(l) is { Success: true } m && m.Groups["name"].Value == region
        );
        if (open < 0)
        {
            problem = $"the file has no '#region {region}'";
            return null;
        }

        var result = new List<string>();
        var depth = 1;
        for (var i = open + 1; i < lines.Length; i++)
        {
            if (RegionStart.IsMatch(lines[i]))
            {
                depth++;
                continue;
            }
            if (RegionEnd.IsMatch(lines[i]))
            {
                if (--depth == 0)
                    return result;
                continue;
            }
            result.Add(lines[i]);
        }

        problem = $"'#region {region}' has no matching '#endregion'";
        return null;
    }

    private static bool IsRegionMarker(string line) =>
        RegionStart.IsMatch(line) || RegionEnd.IsMatch(line);

    /// <summary>
    /// The first index at or after <paramref name="from"/> where <paramref name="chunk"/> occurs
    /// as consecutive lines of <paramref name="target"/>, compared without their common
    /// indentation. -1 when it does not.
    /// </summary>
    private static int Find(List<string> target, List<string> chunk, int from)
    {
        for (var i = from; i + chunk.Count <= target.Count; i++)
        {
            if (target[i].Trim() != chunk[0].Trim())
                continue;
            if (
                Dedent(target.GetRange(i, chunk.Count)).SequenceEqual(chunk, StringComparer.Ordinal)
            )
                return i;
        }
        return -1;
    }

    /// <summary>Trailing whitespace trimmed, and the indentation every non-blank line shares removed.</summary>
    private static List<string> Dedent(List<string> lines)
    {
        var trimmed = lines.Select(l => l.TrimEnd()).ToList();
        var indent = trimmed
            .Where(l => l.Length > 0)
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();
        return trimmed.Select(l => l.Length >= indent ? l[indent..] : "").ToList();
    }
}
