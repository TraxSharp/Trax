using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

public sealed class NoPrincipal : IOwnerPrincipal
{
    public int? CurrentAccountId => null;
}
