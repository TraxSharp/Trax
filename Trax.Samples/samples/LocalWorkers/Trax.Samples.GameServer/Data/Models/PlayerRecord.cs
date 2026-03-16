using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Attributes;

namespace Trax.Samples.GameServer.Data.Models;

/// <summary>
/// A player profile stored in the game database. Automatically exposed as a
/// paginated, filterable, sortable GraphQL query via [TraxQueryModel].
/// </summary>
[TraxQueryModel(
    Namespace = GraphQLNamespaces.Players,
    Description = "Player profiles with rank, wins, losses, and rating"
)]
[Table("player_records", Schema = "game")]
public class PlayerRecord
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("player_id")]
    public string PlayerId { get; set; } = "";

    [Column("display_name")]
    public string DisplayName { get; set; } = "";

    [Column("rank")]
    public int Rank { get; set; }

    [Column("wins")]
    public int Wins { get; set; }

    [Column("losses")]
    public int Losses { get; set; }

    [Column("rating")]
    public int Rating { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
