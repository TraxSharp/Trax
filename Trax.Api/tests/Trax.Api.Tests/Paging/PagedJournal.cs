using Trax.Effect.Attributes;

namespace Trax.Api.Tests.Paging;

[TraxAllowAnonymous]
[TraxQueryModel(Paging = TraxPaging.Cursor, Name = "journals")]
public class PagedJournal
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
