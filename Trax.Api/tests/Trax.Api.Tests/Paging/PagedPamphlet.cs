using Trax.Effect.Attributes;

namespace Trax.Api.Tests.Paging;

[TraxAllowAnonymous]
[TraxQueryModel(Paging = TraxPaging.None, Name = "pamphlets")]
public class PagedPamphlet
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
