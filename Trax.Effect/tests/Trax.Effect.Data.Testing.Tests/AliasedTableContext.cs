using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

/// <summary>
/// A filtered per-user table, and a second entity type reading the same rows through a view
/// mapping with no filter and no owner key.
/// </summary>
public sealed class AliasedTableContext(
    DbContextOptions<AliasedTableContext> options,
    IOwnerPrincipal principal
) : OwnerScopeContext(options, principal)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder
            .Entity<Note>()
            .ToTable("notes")
            .HasQueryFilter(n => n.AccountId == Principal.CurrentAccountId);
        modelBuilder.Entity<NoteView>().ToView("notes");
    }
}
