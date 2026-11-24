using System.ComponentModel.DataAnnotations.Schema;
using Trax.Core.Extensions;

namespace Trax.Effect.Models.Drain;

/// <summary>
/// Base model for <c>trax.drain</c>: the one row that says every host sharing the database stops starting new work,
/// and until when. The job dispatcher dispatches nothing, the manifest manager queues no scheduled run and no local
/// worker claims a job while it holds; what is already running carries on. A deploy sets it before it replaces the
/// hosts, and an operator can set it from the console or the API.
/// </summary>
/// <remarks>
/// <para>
/// A singleton: at most one row, with <see cref="Id"/> always <see cref="SingletonId"/>, which a CHECK constraint
/// holds. No row means no drain. A row whose <see cref="Until"/> has passed means no drain either, so a deploy that
/// crashed or was interrupted after setting it stops pausing the hosts on its own. Ending a drain deletes the row.
/// </para>
/// <para>
/// EF Core mapping lives in <c>Trax.Effect.Data.Models.Drain.PersistentDrain</c>; the table ships in the core migration
/// set (Postgres <c>091</c>, SQLite <c>050</c>).
/// </para>
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/configuration/trax-drain">SDK reference</seealso>
public class Drain
{
    /// <summary>The singleton row's id. Always 1.</summary>
    public const long SingletonId = 1L;

    /// <summary>The row's key. Always <see cref="SingletonId"/>; the database refuses any other value.</summary>
    [Column("id")]
    public long Id { get; set; } = SingletonId;

    /// <summary>
    /// When the drain lapses, in UTC. A host compares it with its own clock, so the drain holds while the host's time
    /// is before it.
    /// </summary>
    [Column("until")]
    public DateTime Until { get; set; }

    /// <summary>When the drain was set, in UTC.</summary>
    [Column("started_at")]
    public DateTime StartedAt { get; set; }

    /// <summary>
    /// Who set it: <c>deploy</c> for a process started with <c>--trax-drain</c>, or the operator's principal id for a
    /// drain set from the console or the API.
    /// </summary>
    [Column("started_by")]
    public string StartedBy
    {
        get => field ?? string.Empty;
        set;
    }

    /// <summary>
    /// Whether a drain that lapses at <paramref name="until"/> holds at <paramref name="now"/>. A stored time read back
    /// without a kind (SQLite) is taken as UTC, which is how it was written.
    /// </summary>
    internal static bool Holds(DateTime until, DateTimeOffset now) =>
        now.UtcDateTime < DateTime.SpecifyKind(until, DateTimeKind.Utc);
}
