namespace Trax.Effect.Data.Utils;

/// <summary>
/// The LIKE pattern for a case-insensitive search for text inside a column, in the one shape the
/// Postgres trigram indexes serve: <c>lower(column) LIKE pattern ESCAPE '\'</c>. With EF Core that
/// is <c>EF.Functions.Like(column.ToLower(), LikePattern.Contains(term), LikePattern.Escape)</c>.
/// </summary>
/// <remarks>
/// A caller's term is data, never pattern syntax: its <c>%</c>, <c>_</c> and the escape character
/// itself are escaped, so a search for <c>50%</c> finds the text <c>50%</c> and not every value
/// containing <c>50</c>. Lowering both sides is the case-insensitive match every provider
/// translates, and the trigram indexes are built over the lowered column, so a search written any
/// other way reads the whole table.
/// </remarks>
public static class LikePattern
{
    /// <summary>The escape character of the patterns <see cref="Contains"/> builds.</summary>
    public const string Escape = "\\";

    /// <summary>
    /// A LIKE pattern matching any text that contains <paramref name="term"/>, lowered, with the
    /// wildcards <c>%</c> and <c>_</c> and the escape character itself escaped.
    /// </summary>
    /// <param name="term">The text to find, as the caller typed it.</param>
    public static string Contains(string term)
    {
        ArgumentNullException.ThrowIfNull(term);
        return "%"
            + term.ToLowerInvariant()
                .Replace(Escape, Escape + Escape, StringComparison.Ordinal)
                .Replace("%", Escape + "%", StringComparison.Ordinal)
                .Replace("_", Escape + "_", StringComparison.Ordinal)
            + "%";
    }
}
