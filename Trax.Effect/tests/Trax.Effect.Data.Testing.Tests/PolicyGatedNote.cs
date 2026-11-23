using Microsoft.EntityFrameworkCore;
using Trax.Core.Extensions;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

[TraxQueryModel]
public class PolicyGatedNote : IPolicyGated
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public required Account Account { get; set; }
}
