using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Effect.Data.Testing.Tests;

// A consumer's shape for the owner-scope census: an owner entity (Account), the accessor a row
// filter reads the current principal through (IOwnerPrincipal), and entities that hold per-user
// data in each of the ways the census has to recognise. Each context maps one scenario, so a test
// names exactly the offender it expects.

public interface IOwnerPrincipal
{
    int? CurrentAccountId { get; }
}

public sealed class NoPrincipal : IOwnerPrincipal
{
    public int? CurrentAccountId => null;
}

public class Account
{
    public int Id { get; set; }
}

public class Note
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
}

public class PinnedNote : Note
{
    public int Position { get; set; }
}

public class Poll
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
}

public class Answer
{
    public int Id { get; set; }
    public int PollId { get; set; }
    public Poll Poll { get; set; } = null!;
}

public class Article
{
    public int Id { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class AuditRow
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
}

[TraxQueryModel]
[TraxAuthorize]
public class ExposedNote
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
}

[TraxQueryModel]
[TraxAllowAnonymous]
public class AnonymousNote
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
}

[TraxQueryModel]
public class UndeclaredNote
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
}

[TraxQueryModel]
[TraxAuthorize(Roles = "subscriber")]
public class RoleGatedNote
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
}

[TraxAuthorize("premium")]
public interface IPolicyGated;

[TraxQueryModel]
public class PolicyGatedNote : IPolicyGated
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
}

[TraxQueryModel]
[TraxAuthorize(Roles = "editor")]
public class PublishedArticle
{
    public int Id { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public abstract class OwnerScopeContext(DbContextOptions options, IOwnerPrincipal principal)
    : DbContext(options)
{
    protected IOwnerPrincipal Principal { get; } = principal;

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Account>().HasQueryFilter(a => a.Id == Principal.CurrentAccountId);
}

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

[TraxQueryModel]
[TraxAuthorize(Roles = "premium")]
public class PremiumNote
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public Account Account { get; set; } = null!;
}

[TraxQueryModel]
[TraxAuthorize(Roles = "admin")]
public class NoteView
{
    public int Id { get; set; }
    public int AccountId { get; set; }
}

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
