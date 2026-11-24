using Trax.Effect.Attributes;

namespace Trax.Api.Tests.Paging;

[TraxAllowAnonymous]
[TraxQueryModel(Name = "notebooks")]
public class PagedNotebook
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
