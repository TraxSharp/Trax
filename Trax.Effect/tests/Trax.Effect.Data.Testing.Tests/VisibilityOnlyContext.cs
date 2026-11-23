using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

/// <summary>An owner foreign key whose only filter is a soft-delete rule.</summary>
public sealed class VisibilityOnlyContext(
    DbContextOptions<VisibilityOnlyContext> options,
    IOwnerPrincipal principal
) : OwnerScopeContext(options, principal)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Note>().HasQueryFilter(n => n.Account.Id > 0);
    }
}
