using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

/// <summary>
/// EF10 named filters: a note carries an owner-scope filter and a visibility filter under names of
/// their own, so query code can switch one off and leave the other on.
/// </summary>
public sealed class NamedFilterContext(
    DbContextOptions<NamedFilterContext> options,
    IOwnerPrincipal principal
) : OwnerScopeContext(options, principal)
{
    public const string OwnerFilter = "Owner";
    public const string VisibleFilter = "Visible";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder
            .Entity<Note>()
            .HasQueryFilter(OwnerFilter, n => n.AccountId == Principal.CurrentAccountId)
            .HasQueryFilter(VisibleFilter, n => n.Id > 0);
        modelBuilder.Entity<Article>().HasQueryFilter(a => a.DeletedAt == null);
    }
}
