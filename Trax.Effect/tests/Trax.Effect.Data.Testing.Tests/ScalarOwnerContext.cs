using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

/// <summary>A scalar owner id with no foreign key and no filter.</summary>
public sealed class ScalarOwnerContext(
    DbContextOptions<ScalarOwnerContext> options,
    IOwnerPrincipal principal
) : OwnerScopeContext(options, principal)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<AuditRow>();
    }
}
