using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Attributes;

namespace Trax.Samples.GameServer.Data.Models;

/// <summary>
/// A public game-server announcement (patch notes, event reveals, etc.). Exposed
/// via GraphQL with <see cref="TraxAllowAnonymousAttribute"/> so any caller —
/// authenticated or not — can read announcement copy without needing an API
/// key. Mirrors how a live game's marketing surface usually works: the news
/// feed is open even when the rest of the API requires login.
///
/// <para>
/// The optional <see cref="RelatedMatch"/> navigation links an announcement to
/// a <see cref="MatchRecord"/> (Admin-only). This is the Option B no-cascade
/// fixture: anonymous callers can read the announcement title and body, but
/// the moment a query asks for <c>relatedMatch { ... }</c> it crosses into
/// admin-only territory and the request is rejected. The anonymous parent
/// does not propagate openness to its gated children.
/// </para>
/// </summary>
[TraxQueryModel(
    Namespace = GraphQLNamespaces.Public,
    Description = "Public announcements (anonymous-readable)."
)]
[TraxAllowAnonymous]
[Table("public_announcements", Schema = "game")]
public class PublicAnnouncement
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("title")]
    public string Title { get; set; } = "";

    [Column("body")]
    public string Body { get; set; } = "";

    [Column("published_at")]
    public DateTime PublishedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional FK to a gated <see cref="MatchRecord"/>. Populated for
    /// announcements like "Replay: Match XYZ" where the related match record
    /// is admin-only data; nullable for general announcements that have no
    /// match association.
    /// </summary>
    [Column("related_match_id")]
    public long? RelatedMatchId { get; set; }

    public MatchRecord? RelatedMatch { get; set; }
}
