using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

/// <summary>The same role-gated per-user entity with no row filter.</summary>
public sealed class GatedUnfilteredContext(
    DbContextOptions<GatedUnfilteredContext> options,
    IOwnerPrincipal principal
) : OwnerScopeContext(options, principal)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<PremiumNote>();
    }
}
