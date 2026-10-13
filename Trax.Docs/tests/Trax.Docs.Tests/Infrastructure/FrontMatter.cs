namespace Trax.Docs.Tests.Infrastructure;

/// <summary>
/// A page's YAML front matter, read as the subset the docs use: top-level <c>key: value</c>
/// lines, where a value is plain, single or double quoted, a flow list (<c>[a, b]</c>), or empty
/// with a block list (<c>- a</c> lines) under it. A <c> #</c> comment after a plain value is
/// dropped. Anything else (nested maps, block scalars) is kept as its raw text and not
/// interpreted, which no docs field needs.
/// </summary>
internal sealed class FrontMatter
{
    private static readonly Regex Block = new(
        @"\A---[ \t]*\n(?:(?<body>.*?)\n)?---[ \t]*(?:\n|\z)",
        RegexOptions.Compiled | RegexOptions.Singleline
    );

    private static readonly Regex KeyLine = new(
        @"^(?<key>[A-Za-z_][A-Za-z0-9_-]*):(?:[ \t]+(?<value>.*?))?[ \t]*$",
        RegexOptions.Compiled
    );

    private static readonly Regex ListItem = new(
        @"^[ \t]+-[ \t]+(?<item>.*?)[ \t]*$",
        RegexOptions.Compiled
    );

    private readonly Dictionary<string, List<string>> _values;

    private FrontMatter(bool exists, Dictionary<string, List<string>> values)
    {
        Exists = exists;
        _values = values;
    }

    /// <summary>Whether the page opens with a front matter block at all.</summary>
    public bool Exists { get; }

    public IEnumerable<string> Keys => _values.Keys;

    public bool Has(string key) => _values.ContainsKey(key);

    /// <summary>
    /// The key's value as one string: a scalar as written (quotes removed), or null when the key
    /// is absent, empty, or holds a list.
    /// </summary>
    public string? Scalar(string key) =>
        _values.TryGetValue(key, out var v) && v.Count == 1 && !IsList(key) ? v[0] : null;

    /// <summary>
    /// The key's values as a list: each item of a flow or block list, or a scalar as a list of
    /// one. Empty when the key is absent or empty.
    /// </summary>
    public IReadOnlyList<string> List(string key) => _values.TryGetValue(key, out var v) ? v : [];

    private readonly HashSet<string> _lists = new(StringComparer.Ordinal);

    private bool IsList(string key) => _lists.Contains(key);

    public static FrontMatter Of(string markdown)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var match = Block.Match(markdown.Replace("\r\n", "\n"));
        var result = new FrontMatter(match.Success, values);
        if (!match.Success)
            return result;

        string? listKey = null;
        foreach (var line in match.Groups["body"].Value.Split('\n'))
        {
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
                continue;

            var item = ListItem.Match(line);
            if (item.Success && listKey is not null)
            {
                values[listKey].Add(Unquote(item.Groups["item"].Value));
                continue;
            }

            var key = KeyLine.Match(line);
            if (!key.Success)
            {
                listKey = null;
                continue;
            }

            var name = key.Groups["key"].Value;
            var value = key.Groups["value"].Value;
            var list = new List<string>();
            values[name] = list;
            listKey = null;

            if (value.Length == 0)
            {
                // A block list may follow; until one does, the key is present and empty.
                listKey = name;
                result._lists.Add(name);
            }
            else if (value.StartsWith('[') && value.EndsWith(']'))
            {
                result._lists.Add(name);
                list.AddRange(
                    value[1..^1]
                        .Split(
                            ',',
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                        )
                        .Select(Unquote)
                );
            }
            else
                list.Add(Unquote(value));
        }

        return result;
    }

    /// <summary>The page's front matter, read from its file.</summary>
    public static FrontMatter OfPage(string page) =>
        Of(File.ReadAllText(RepoRoot.Combine(page.Split('/'))));

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            return value[1..^1];

        // A comment after a plain value: ` #` starts it, a bare `#` inside a word does not.
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return comment >= 0 ? value[..comment].TrimEnd() : value;
    }
}
