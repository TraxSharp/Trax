namespace Trax.Dashboard.Utilities;

/// <summary>
/// The indented form of the JSON a detail page shows, kept per field. A detail page re-renders
/// on every poll tick and every interaction, and a stored input or output can be megabytes, so
/// parsing and re-indenting it each time costs more than the rest of the render. A field is
/// formatted again only when its text changes.
/// </summary>
internal sealed class JsonDisplayCache
{
    private readonly Dictionary<string, (string Raw, string Formatted)> _fields = [];

    /// <summary>
    /// <paramref name="json"/> indented by <see cref="DashboardFormatters.FormatJson"/>, from the
    /// cache when <paramref name="field"/> last held the same text.
    /// </summary>
    public string Format(string field, string json)
    {
        if (
            _fields.TryGetValue(field, out var cached)
            && (
                ReferenceEquals(cached.Raw, json)
                || string.Equals(cached.Raw, json, StringComparison.Ordinal)
            )
        )
            return cached.Formatted;

        var formatted = DashboardFormatters.FormatJson(json);
        _fields[field] = (json, formatted);
        return formatted;
    }
}
