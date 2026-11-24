using Trax.Effect.Attributes;

namespace Trax.Api.Tests.Paging;

/// <summary>A model whose paging style is not a defined <see cref="TraxPaging"/> value.</summary>
[TraxAllowAnonymous]
[TraxQueryModel(Paging = (TraxPaging)99, Name = "scrolls")]
public class PagedScroll
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
