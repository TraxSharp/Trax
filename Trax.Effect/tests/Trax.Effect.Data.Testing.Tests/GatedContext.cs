using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

/// <summary>A role-gated per-user entity that is also filtered through the principal.</summary>
public sealed class GatedContext(DbContextOptions<GatedContext> options, IOwnerPrincipal principal)
    : OwnerScopeContext(options, principal)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder
            .Entity<PremiumNote>()
            .HasQueryFilter(n => n.AccountId == Principal.CurrentAccountId);
    }
}
