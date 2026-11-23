using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

public abstract class OwnerScopeContext(DbContextOptions options, IOwnerPrincipal principal)
    : DbContext(options)
{
    protected IOwnerPrincipal Principal { get; } = principal;

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Account>().HasQueryFilter(a => a.Id == Principal.CurrentAccountId);
}
