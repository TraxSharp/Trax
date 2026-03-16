using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Attributes;

namespace Trax.Samples.GameServer.Data.Models;

/// <summary>
/// A completed match record. Exposed as a GraphQL query with pagination and
/// filtering but without sorting (demonstrates per-model feature configuration).
/// </summary>
[TraxQueryModel(
    Namespace = GraphQLNamespaces.Matches,
    Description = "Match history",
    Sorting = false
)]
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
