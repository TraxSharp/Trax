namespace Trax.Adr.Guard;

/// <summary>
/// The YAML subset an ADR frontmatter block is allowed to use: scalar values, flow lists
/// (<c>[a, b]</c>) and block lists (<c>- a</c> on following lines). Anything else is a
/// parse error rather than a silently ignored key.
/// </summary>
/// <remarks>
/// A full YAML parser is deliberately not used. The accepted subset is the contract, and
/// a parser that accepts more than the contract would let an ADR carry structure the
/// format spec does not describe and no other tool would read the same way.
/// </remarks>
public sealed class Frontmatter
{
    private readonly Dictionary<string, string> _scalars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _lists = new(StringComparer.Ordinal);
    private readonly List<string> _keys = [];

    private Frontmatter(string? error = null) => Error = error;

    /// <summary>Null when the document parsed cleanly; otherwise why it did not.</summary>
    public string? Error { get; }

    public bool Parsed => Error is null;

    /// <summary>Every key present in the block, in the order they appeared.</summary>
    public IReadOnlyList<string> Keys => _keys;

    public string? Scalar(string key) => _scalars.GetValueOrDefault(key);

    /// <summary>
    /// The values of a list-valued key, or null when the key is absent or scalar. A scalar
    /// is NOT coerced into a one-element list: <c>areas: migrations</c> is a different
    /// shape from <c>areas: [migrations]</c>, and quietly accepting both would let the two
    /// drift apart across the corpus.
    /// </summary>
    public IReadOnlyList<string>? List(string key) =>
        _lists.TryGetValue(key, out var values) ? values : null;

    public bool Has(string key) => _scalars.ContainsKey(key) || _lists.ContainsKey(key);

    /// <summary>
    /// Extracts and parses the leading <c>---</c> delimited block. Returns a failed
    /// instance (never null) when there is no block or it is malformed.
    /// </summary>
    public static Frontmatter Parse(string documentText)
    {
        var text = documentText.Replace("\r\n", "\n");

        if (!text.StartsWith("---\n", StringComparison.Ordinal))
            return new Frontmatter(
                "no frontmatter block: the document must open with a '---' line"
            );

        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0)
            return new Frontmatter("the frontmatter block is never closed by a '---' line");

        return ParseBody(text[4..(end + 1)].Split('\n'));
    }

    private static Frontmatter ParseBody(string[] lines)
    {
        var result = new Frontmatter();
        string? pendingListKey = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0 || line.TrimStart().StartsWith('#'))
                continue;

            var trimmed = line.TrimStart();

            // A block-list item continues the key declared on a previous line.
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                if (pendingListKey is null)
                    return new Frontmatter(
                        $"line {i + 1}: list item '{trimmed}' does not follow a key"
                    );

                result._lists[pendingListKey].Add(Unquote(trimmed[2..].Trim()));
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
                return new Frontmatter(
                    $"line {i + 1}: '{line}' is neither 'key: value' nor a '- item'"
                );

            var key = line[..colon];
            if (key.Trim() != key)
                return new Frontmatter($"line {i + 1}: key '{key}' must not be indented or padded");

            if (result.Has(key))
                return new Frontmatter($"line {i + 1}: key '{key}' appears twice");

            result._keys.Add(key);
            var value = line[(colon + 1)..].Trim();

            if (value.Length == 0)
            {
                // 'key:' alone opens a block list. An empty scalar is not a valid shape.
                result._lists[key] = [];
                pendingListKey = key;
                continue;
            }

            pendingListKey = null;

            if (value.StartsWith('[') && value.EndsWith(']'))
            {
                var inner = value[1..^1].Trim();
                result._lists[key] =
                    inner.Length == 0
                        ? []
                        : inner.Split(',').Select(v => Unquote(v.Trim())).ToList();
                continue;
            }

            if (value.StartsWith('[') || value.EndsWith(']'))
                return new Frontmatter($"line {i + 1}: key '{key}' has an unterminated flow list");

            result._scalars[key] = Unquote(value);
        }

        return result;
    }

    private static string Unquote(string value) =>
        value.Length >= 2
        && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;
}
