using Microsoft.EntityFrameworkCore;
using Trax.Core.Extensions;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

[TraxQueryModel]
[TraxAuthorize(Roles = "premium")]
public class PremiumNote
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public required Account Account { get; set; }
}
