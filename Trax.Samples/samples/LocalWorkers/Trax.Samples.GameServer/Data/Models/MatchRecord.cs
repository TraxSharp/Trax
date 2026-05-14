using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Attributes;
using Trax.Samples.GameServer.Auth;

namespace Trax.Samples.GameServer.Data.Models;

/// <summary>
/// A completed match record. Exposed as a GraphQL query with pagination and
/// filtering but without sorting (demonstrates per-model feature configuration).
///
/// <para>
/// Gated with <see cref="TraxAuthorizeAttribute"/>: only callers carrying the
/// <see cref="GameRole.Admin"/> role can read match history. The attribute
/// attaches the GraphQL <c>@authorize</c> directive at both the entry field
/// (<c>discover.matches.matchRecords</c>) and the <c>ObjectType</c> level —
/// the latter means transitive access through a navigation property on an
/// ungated type would also be blocked. Connection-shaped scalars like
/// <c>totalCount</c> and <c>pageInfo</c> are gated too; an unauthorized
/// caller cannot enumerate match cardinality.
/// </para>
/// </summary>
[TraxQueryModel(
    Namespace = GraphQLNamespaces.Matches,
    Description = "Match history (admin only)",
    Sorting = false
)]
[TraxAuthorize(Roles = nameof(GameRole.Admin))]
[Table("match_records", Schema = "game")]
public class MatchRecord
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("match_id")]
    public string MatchId { get; set; } = "";

    [Column("region")]
    public string Region { get; set; } = "";

    [Column("winner_id")]
    public string WinnerId { get; set; } = "";

    [Column("loser_id")]
    public string LoserId { get; set; } = "";

    [Column("winner_score")]
    public int WinnerScore { get; set; }

    [Column("loser_score")]
    public int LoserScore { get; set; }

    [Column("played_at")]
    public DateTime PlayedAt { get; set; } = DateTime.UtcNow;
}
