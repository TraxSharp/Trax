using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

/// <summary>Owner-scoped entities exposed over GraphQL with each posture the census refuses.</summary>
public sealed class PostureContext(
    DbContextOptions<PostureContext> options,
    IOwnerPrincipal principal
) : OwnerScopeContext(options, principal)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder
            .Entity<AnonymousNote>()
            .HasQueryFilter(n => n.AccountId == Principal.CurrentAccountId);
        modelBuilder
            .Entity<UndeclaredNote>()
            .HasQueryFilter(n => n.AccountId == Principal.CurrentAccountId);
        modelBuilder
            .Entity<RoleGatedNote>()
            .HasQueryFilter(n => n.AccountId == Principal.CurrentAccountId);
        modelBuilder
            .Entity<PolicyGatedNote>()
            .HasQueryFilter(n => n.AccountId == Principal.CurrentAccountId);
    }
}
