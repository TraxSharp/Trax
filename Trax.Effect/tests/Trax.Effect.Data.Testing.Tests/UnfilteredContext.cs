using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

/// <summary>A per-user entity with an owner foreign key and no row filter.</summary>
public sealed class UnfilteredContext(
    DbContextOptions<UnfilteredContext> options,
    IOwnerPrincipal principal
) : OwnerScopeContext(options, principal)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Note>();
    }
}
