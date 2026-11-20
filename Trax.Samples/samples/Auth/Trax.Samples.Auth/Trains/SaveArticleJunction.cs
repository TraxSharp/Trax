using Trax.Api.Auth;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Auth.Auth;
using Trax.Samples.Auth.Data;

namespace Trax.Samples.Auth.Trains;

public class SaveArticleJunction(TraxPrincipal caller, INewsroomDbContext db)
    : Junction<PublishArticleInput, PublishArticleOutput>
{
    public override async Task<PublishArticleOutput> Run(PublishArticleInput input)
    {
        var article = new Article
        {
            Title = input.Title,
            Body = input.Body,
            AuthorId = caller.Id,
            PublishedAt = DateTime.UtcNow,
            EditorNote = input.EditorNote is null
                ? null
                : new EditorNote { Text = input.EditorNote },
        };

        db.Articles.Add(article);
        await db.SaveChangesAsync().ConfigureAwait(false);

        return new PublishArticleOutput(article.Id, article.AuthorId);
    }
}
