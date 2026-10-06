using HotChocolate;
using HotChocolate.Types;
using Trax.Effect.Attributes;

namespace Trax.Samples.Auth.Data;

/// <summary>
/// Adds a computed <c>wordCount</c> field to <see cref="Article"/>.
/// </summary>
/// <remarks>
/// <see cref="Article"/> is <c>[TraxAllowAnonymous]</c>, so a field grafted onto it inherits no
/// gate and must declare its own posture, or the host refuses to start. It is public on purpose:
/// it is computed from the body, which the same anonymous caller can already read. Use Trax's
/// attributes here; HotChocolate's <c>[Authorize]</c> and <c>[AllowAnonymous]</c> are refused at
/// startup.
/// </remarks>
[ExtendObjectType(typeof(Article))]
public class ArticleExtensions
{
    [TraxAllowAnonymous]
    public int GetWordCount(
        // Projection fetches only the columns a query selects. Name the ones this resolver reads,
        // or a query asking for wordCount alone gets an article with an empty body.
        [Parent(requires: nameof(Article.Body))] Article article
    ) => article.Body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
