using System.ComponentModel.DataAnnotations.Schema;
using Trax.Effect.Attributes;

namespace Trax.Samples.Auth.Data;

/// <summary>
/// A published article, readable by anyone. <c>[TraxAllowAnonymous]</c> declares that on purpose:
/// a <c>[TraxQueryModel]</c> with neither it nor <c>[TraxAuthorize]</c> stops the host at startup.
/// </summary>
/// <remarks>
/// Openness does not cascade. <see cref="EditorNote"/> is gated, so an anonymous query that
/// selects <c>editorNote</c>, or filters or sorts through it, is refused even though the article
/// itself is public.
/// </remarks>
[TraxQueryModel(Namespace = "news", Description = "Published articles, readable by anyone.")]
[TraxAllowAnonymous]
[Table("articles")]
public class Article
{
    public long Id { get; set; }

    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    /// <summary>
    /// The scheme-qualified principal id of whoever published it, for example
    /// <c>TraxJwt:alice</c>. Store the id as the claim carries it, scheme included.
    /// </summary>
    public string AuthorId { get; set; } = "";

    public DateTime PublishedAt { get; set; }

    public long? EditorNoteId { get; set; }

    public EditorNote? EditorNote { get; set; }
}
