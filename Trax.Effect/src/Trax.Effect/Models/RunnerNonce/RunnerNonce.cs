using System.ComponentModel.DataAnnotations.Schema;

namespace Trax.Effect.Models.RunnerNonce;

/// <summary>
/// Base model for <c>trax.runner_nonce</c>: a nonce a Trax runner accepted on a signed request.
/// Trax.Scheduler's runners record one here per signed synchronous request, so every instance of a
/// runner that shares the database accepts a request once between them.
/// </summary>
/// <remarks>
/// EF Core mapping lives in
/// <see cref="Trax.Effect.Data.Models.RunnerNonce.PersistentRunnerNonce"/>. The table ships in the
/// core migration set (Postgres <c>047</c>, Sqlite <c>012</c>), and the Scheduler reaches it through
/// <c>IDataContext.RunnerNonces</c> rather than through SQL of its own.
/// </remarks>
public class RunnerNonce
{
    /// <summary>
    /// The nonce from a signature whose MAC and timestamp verified. The primary key, so a second
    /// insert of the same nonce is refused by the database.
    /// </summary>
    [Column("nonce")]
    public string Nonce { get; set; } = null!;

    /// <summary>
    /// When the record may be forgotten: the signature's timestamp plus the runner's allowed clock
    /// skew. After it the timestamp alone refuses the request, so an expired row refuses nothing and
    /// may be taken over or deleted. Stored as Unix seconds, so it keeps whole seconds only.
    /// </summary>
    [Column("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }
}
