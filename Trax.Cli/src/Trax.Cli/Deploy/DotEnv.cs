namespace Trax.Cli.Deploy;

/// <summary>
/// Reads a <c>.env</c> file as compose does: <c>NAME=value</c> lines, <c>#</c> comments and blank lines skipped, an
/// optional <c>export</c> before the name, and a value in matching single or double quotes taken without them.
/// </summary>
internal static class DotEnv
{
    public static async Task<IReadOnlyDictionary<string, string>> ReadAsync(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return values;
        foreach (var raw in await File.ReadAllLinesAsync(path).ConfigureAwait(false))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (line.StartsWith("export ", StringComparison.Ordinal))
                line = line["export ".Length..].TrimStart();
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
                continue;
            values[line[..eq].Trim()] = Unquote(line[(eq + 1)..].Trim());
        }
        return values;
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] is '"' or '\'') && value[^1] == value[0]
            ? value[1..^1]
            : value;
}
