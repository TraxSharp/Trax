using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

[TraxQueryModel]
[TraxAuthorize(Roles = "editor")]
public class PublishedArticle
{
    public int Id { get; set; }
    public DateTime? DeletedAt { get; set; }
}
