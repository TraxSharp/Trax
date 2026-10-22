namespace Trax.Skills.Publish;

/// <summary>
/// The frontmatter of a <c>SKILL.md</c>: top-level <c>key: value</c> scalars and one
/// <c>metadata:</c> map of indented scalars. Anything else is a parse error.
/// </summary>
/// <remarks>
/// A full YAML parser is deliberately not used, as in <c>Trax.Adr.Guard</c>. The subset is the
/// contract: the skills CLI and Claude Code both read these files, and a block either of them
/// could read differently from this one is a block nobody can check.
/// </remarks>
public sealed class SkillFrontmatter
{
    private static readonly Regex TopLevel = new(@"^(?<key>[a-z][a-z0-9-]*):(?: (?<value>.*))?$");
    private static readonly Regex Nested = new(@"^  (?<key>[a-z][a-z0-9-]*): (?<value>.+)$");

    private readonly Dictionary<string, string> _scalars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _metadata = new(StringComparer.Ordinal);

    private SkillFrontmatter(string? error, string body)
    {
        Error = error;
        Body = body;
    }

    /// <summary>Null when the block parsed; otherwise why it did not.</summary>
    public string? Error { get; }

    /// <summary>Everything after the closing <c>---</c> line.</summary>
    public string Body { get; }

    public IReadOnlyCollection<string> Keys => _scalars.Keys;

    public IReadOnlyDictionary<string, string> Metadata => _metadata;

    public string? Scalar(string key) => _scalars.GetValueOrDefault(key);

    public static SkillFrontmatter Parse(string text)
    {
        text = text.Replace("\r\n", "\n");
        if (!text.StartsWith("---\n", StringComparison.Ordinal))
            return new SkillFrontmatter("it does not start with a '---' frontmatter block", text);

        var end = text.IndexOf("\n---\n", 3, StringComparison.Ordinal);
        if (end < 0)
            return new SkillFrontmatter("its frontmatter block is never closed", text);

        var result = new SkillFrontmatter(null, text[(end + "\n---\n".Length)..]);
        var inMetadata = false;
        foreach (var line in text[4..end].Split('\n'))
        {
            if (inMetadata && Nested.Match(line) is { Success: true } nested)
            {
                if (
                    !result._metadata.TryAdd(
                        nested.Groups["key"].Value,
                        Unquote(nested.Groups["value"].Value)
                    )
                )
                    return Failed($"metadata key '{nested.Groups["key"].Value}' appears twice");
                continue;
            }

            var top = TopLevel.Match(line);
            if (!top.Success)
                return Failed($"line '{line}' is not 'key: value' or an indented metadata entry");

            var key = top.Groups["key"].Value;
            var value = top.Groups["value"].Success ? top.Groups["value"].Value.Trim() : "";
            inMetadata = key == "metadata";
            if (inMetadata ? value.Length > 0 : value.Length == 0)
                return Failed(
                    inMetadata
                        ? "'metadata' must be a map of indented entries"
                        : $"key '{key}' has no value"
                );
            if (!inMetadata && !result._scalars.TryAdd(key, Unquote(value)))
                return Failed($"key '{key}' appears twice");
        }
        return result;

        SkillFrontmatter Failed(string why) => new(why, text);
    }

    /// <summary>
    /// The block written back with <paramref name="metadata"/> in place of the parsed map, the
    /// scalars in the order <paramref name="order"/> gives, and the body unchanged.
    /// </summary>
    public string Render(
        IReadOnlyList<string> order,
        IReadOnlyList<KeyValuePair<string, string>> metadata
    )
    {
        var lines = new List<string> { "---" };
        lines.AddRange(order.Where(_scalars.ContainsKey).Select(k => $"{k}: {_scalars[k]}"));
        if (metadata.Count > 0)
        {
            lines.Add("metadata:");
            lines.AddRange(metadata.Select(m => $"  {m.Key}: {Quote(m.Value)}"));
        }
        lines.Add("---");
        return string.Join('\n', lines) + "\n" + Body;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    private static string Quote(string value) =>
        value is "true" or "false" ? value : $"\"{value}\"";
}
