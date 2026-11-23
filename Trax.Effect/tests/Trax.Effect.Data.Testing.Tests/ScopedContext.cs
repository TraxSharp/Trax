using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

/// <summary>Every per-user entity is filtered through the principal.</summary>
public sealed class ScopedContext(
    DbContextOptions<ScopedContext> options,
    IOwnerPrincipal principal
) : OwnerScopeContext(options, principal)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Note>().HasQueryFilter(n => n.AccountId == Principal.CurrentAccountId);
        modelBuilder.Entity<PinnedNote>();
        modelBuilder.Entity<Poll>().HasQueryFilter(p => p.AccountId == Principal.CurrentAccountId);
        modelBuilder
            .Entity<Answer>()
            .HasQueryFilter(a => a.Poll.AccountId == Principal.CurrentAccountId);
        modelBuilder.Entity<Article>().HasQueryFilter(a => a.DeletedAt == null);
        modelBuilder
            .Entity<ExposedNote>()
            .HasQueryFilter(n => n.AccountId == Principal.CurrentAccountId);
        modelBuilder.Entity<PublishedArticle>().HasQueryFilter(a => a.DeletedAt == null);
    }
}
