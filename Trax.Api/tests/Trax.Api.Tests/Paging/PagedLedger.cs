using Trax.Effect.Attributes;

namespace Trax.Api.Tests.Paging;

[TraxAllowAnonymous]
[TraxQueryModel(Paging = TraxPaging.Offset, Name = "ledgers")]
public class PagedLedger
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
