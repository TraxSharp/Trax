using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Trax.Effect.Data.Postgres.Services.NulCharacterInterceptor;

/// <summary>
/// Replaces the NUL character in every string about to be written, because Postgres refuses it: a
/// <c>text</c> column rejects <c>\0</c> and a <c>jsonb</c> column rejects the <c>\u0000</c> escape.
/// Without this, a run whose output or failure message carries one (a byte buffer rendered as a
/// string, <c>int.Parse</c> quoting its input) cannot write its outcome, and the row is left
/// <c>InProgress</c> for the stale-run reaper to record as failed.
/// </summary>
/// <remarks>
/// Each NUL becomes U+FFFD, the Unicode replacement character, so the stored text still shows
/// that something was there. Only added and modified strings are touched, and only when they hold
/// a NUL. SQLite and the in-memory provider store NUL as it is and do not use this.
/// <para>
/// A value that identifies a row is never touched: a key, a foreign key, an indexed column, or a
/// column the model marks with <see cref="ComparedExactlyAnnotation"/> (a work-queue entry's
/// external id and subject key, whose indexes are declared only in SQL). Rewriting one would make
/// <c>"a\0"</c> the same key as <c>"a\uFFFD"</c>; left as it is, Postgres refuses the write.
/// </para>
/// </remarks>
internal sealed class NulCharacterInterceptor : SaveChangesInterceptor
{
    private const char Replacement = '�';
    private const string JsonNulEscape = "u0000";
    private const string JsonReplacementEscape = "ufffd";

    /// <summary>
    /// The model annotation that marks a string column as compared exactly, so it is never
    /// rewritten. The same literal is set in the work-queue mapping in <c>Trax.Effect.Data</c>.
    /// </summary>
    internal const string ComparedExactlyAnnotation = "Trax:ComparedExactly";

    public static NulCharacterInterceptor Instance { get; } = new();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        Scrub(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Scrub(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Scrub(DbContext? context)
    {
        if (context is null)
            return;

        context.ChangeTracker.DetectChanges();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            foreach (var property in entry.Properties)
                ScrubProperty(property);
        }
    }

    private static void ScrubProperty(PropertyEntry property)
    {
        if (property.CurrentValue is not string value)
            return;

        if (IdentifiesTheRow(property.Metadata))
            return;

        var isJson = string.Equals(
            property.Metadata.GetColumnType(),
            "jsonb",
            StringComparison.OrdinalIgnoreCase
        );

        var scrubbed = isJson ? ScrubJson(value) : ScrubText(value);

        if (!ReferenceEquals(scrubbed, value))
            property.CurrentValue = scrubbed;
    }

    internal static bool IdentifiesTheRow(IReadOnlyProperty property) =>
        property.IsKey()
        || property.IsForeignKey()
        || property.IsIndex()
        || property.FindAnnotation(ComparedExactlyAnnotation)?.Value is true;

    /// <summary>Replaces each raw NUL. Returns the same instance when there is none.</summary>
    internal static string ScrubText(string value) =>
        value.Contains('\0') ? value.Replace('\0', Replacement) : value;

    /// <summary>
    /// Replaces each <c>\u0000</c> escape, and each raw NUL, in JSON text. An escaped backslash
    /// followed by <c>u0000</c> is literal text and is left alone. Returns the same instance when
    /// there is nothing to replace.
    /// </summary>
    internal static string ScrubJson(string value)
    {
        var text = ScrubText(value);

        if (!text.Contains("\\u0000", StringComparison.OrdinalIgnoreCase))
            return ReferenceEquals(text, value) ? value : text;

        var builder = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c != '\\' || i + 1 >= text.Length)
            {
                builder.Append(c);
                continue;
            }

            if (
                i + 5 < text.Length
                && string.Compare(
                    text,
                    i + 1,
                    JsonNulEscape,
                    0,
                    JsonNulEscape.Length,
                    StringComparison.OrdinalIgnoreCase
                ) == 0
            )
            {
                builder.Append('\\').Append(JsonReplacementEscape);
                i += JsonNulEscape.Length;
                continue;
            }

            // Any other escape is copied whole, so the character after an escaped backslash is
            // never read as the start of an escape.
            builder.Append(c).Append(text[i + 1]);
            i++;
        }

        return builder.ToString();
    }
}
