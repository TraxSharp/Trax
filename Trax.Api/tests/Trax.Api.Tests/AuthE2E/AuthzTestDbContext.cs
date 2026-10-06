using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Test entities for type-level <c>[TraxAuthorize]</c> on <c>[TraxQueryModel]</c>.
/// Lives in its own <c>test_authz</c> schema so the data does not collide with
/// the unauthorized fixtures in <see cref="TestDbContext"/>.
///
/// The relationship between <see cref="Owner"/> and <see cref="OwnedBook"/> is
/// the centerpiece of the transitive-navigation security test: the parent
/// type is ungated, the child type carries <c>[TraxAuthorize(Roles="Admin")]</c>,
/// and the E2E suite verifies that a Player principal cannot reach the child
/// through the parent's <c>books</c> navigation property.
/// </summary>
[TraxAllowAnonymous]
[TraxQueryModel(Namespace = "vault", Description = "Owners of vault items (ungated).")]
[Table("owners", Schema = "test_authz")]
public class Owner
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    public List<OwnedBook> Books { get; set; } = new();

    /// <summary>
    /// Navigation to anonymous-readable bulletin entries owned by this Owner.
    /// Owner is itself ungated; reaching <see cref="PublicBook"/> through this
    /// navigation must succeed for anonymous callers because both sides allow
    /// anonymous access.
    /// </summary>
    public List<PublicBook> PublicBooks { get; set; } = new();
}

/// <summary>
/// Child entity gated with <c>[TraxAuthorize(Roles="Admin")]</c>. Direct access
/// (top-level <c>ownedBooks</c>) and transitive access (through
/// <c>owners[].books</c>) must both honor the role gate.
/// </summary>
[TraxQueryModel(Namespace = "vault", Description = "Admin-only books.")]
[TraxAuthorize(Roles = "Admin")]
[Table("owned_books", Schema = "test_authz")]
public class OwnedBook
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("owner_id")]
    public long OwnerId { get; set; }

    [Column("title")]
    public string Title { get; set; } = "";

    public Owner Owner { get; set; } = null!;
}

/// <summary>
/// Entity gated with a CSV role list (<c>Roles="Admin,Player"</c>). Both roles
/// satisfy the gate (OR semantics within a single attribute).
/// </summary>
[TraxQueryModel(Namespace = "vault", Description = "Memos visible to Admin or Player.")]
[TraxAuthorize(Roles = "Admin,Player")]
[Table("memos", Schema = "test_authz")]
public class Memo
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("body")]
    public string Body { get; set; } = "";
}

/// <summary>
/// Entity gated with two stacked attributes. AND semantics across attributes:
/// the principal must hold <c>Admin</c> AND satisfy <c>AdminPolicy</c>. Both
/// reduce to the same underlying claim in this test host, so the practical
/// effect is "Admin only with both checks executed." The point of the test is
/// that both directives fire.
/// </summary>
[TraxQueryModel(Namespace = "vault", Description = "Stacked-attribute restricted documents.")]
[TraxAuthorize(Roles = "Admin")]
[TraxAuthorize(Policy = "AdminPolicy")]
[Table("restricted_docs", Schema = "test_authz")]
public class RestrictedDoc
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("payload")]
    public string Payload { get; set; } = "";
}

/// <summary>
/// Entity gated with bare <c>[TraxAuthorize]</c>: any authenticated user passes,
/// anonymous callers fail.
/// </summary>
[TraxQueryModel(Namespace = "vault", Description = "Members-only area.")]
[TraxAuthorize]
[Table("member_areas", Schema = "test_authz")]
public class MemberArea
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";
}

/// <summary>
/// Entity opened to anonymous reads with <c>[TraxAllowAnonymous]</c>. The
/// E2E suite exercises three compositions that exist only because this entity
/// is anonymously readable:
/// <list type="bullet">
/// <item>Direct anonymous read of <c>publicBooks</c> succeeds.</item>
/// <item>Transitive <c>publicBook.linkedOwnedBook</c> still rejects anonymous —
/// the gated child's <c>@authorize</c> directive fires regardless of how the
/// parent opened the cascade. This is the Option B no-cascade contract.</item>
/// <item>Transitive <c>owner.publicBooks</c> succeeds for anonymous callers —
/// reaching an anonymous target through an ungated parent does not raise the
/// gate.</item>
/// </list>
/// </summary>
[TraxQueryModel(Namespace = "vault", Description = "Public bulletin (anonymous-readable).")]
[TraxAllowAnonymous]
[Table("public_books", Schema = "test_authz")]
public class PublicBook
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("owner_id")]
    public long OwnerId { get; set; }

    [Column("title")]
    public string Title { get; set; } = "";

    /// <summary>
    /// Nullable FK to a gated OwnedBook. The transitive-from-anonymous test
    /// requires at least one PublicBook with a real link; rows without a link
    /// prove the resolver does not over-eagerly join.
    /// </summary>
    [Column("linked_owned_book_id")]
    public long? LinkedOwnedBookId { get; set; }

    public Owner Owner { get; set; } = null!;

    public OwnedBook? LinkedOwnedBook { get; set; }
}

public class AuthzTestDbContext(DbContextOptions<AuthzTestDbContext> options) : DbContext(options)
{
    public DbSet<Owner> Owners { get; set; } = null!;
    public DbSet<OwnedBook> OwnedBooks { get; set; } = null!;
    public DbSet<Memo> Memos { get; set; } = null!;
    public DbSet<RestrictedDoc> RestrictedDocs { get; set; } = null!;
    public DbSet<MemberArea> MemberAreas { get; set; } = null!;
    public DbSet<PublicBook> PublicBooks { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("test_authz");

        modelBuilder.Entity<Owner>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasMany(e => e.Books).WithOne(b => b.Owner).HasForeignKey(b => b.OwnerId);
            entity.HasMany(e => e.PublicBooks).WithOne(p => p.Owner).HasForeignKey(p => p.OwnerId);
        });

        modelBuilder.Entity<OwnedBook>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.OwnerId);
        });

        modelBuilder.Entity<PublicBook>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.OwnerId);
            entity
                .HasOne(p => p.LinkedOwnedBook)
                .WithMany()
                .HasForeignKey(p => p.LinkedOwnedBookId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Memo>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<RestrictedDoc>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<MemberArea>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
        });
    }

    /// <summary>
    /// Idempotently provisions the <c>test_authz</c> schema and a deterministic
    /// fixture row set. The owners + books fixture is the key payload for the
    /// transitive-navigation security tests: each owner has at least one book
    /// so the navigation actually attempts to materialize children.
    /// </summary>
    public static void EnsureSeeded(string connectionString)
    {
        AuthE2EHost.EnsureDatabaseExists(
            new Npgsql.NpgsqlConnectionStringBuilder(connectionString).Database!
        );

        var opts = new DbContextOptionsBuilder<AuthzTestDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        using var db = new AuthzTestDbContext(opts);

        // Drop and recreate the schema. Tests reseed every run, so retaining
        // tables across runs has no value, and the drop makes EnsureSeeded
        // robust against schema evolution: a CI database created against an
        // older fixture set (e.g., before PublicBook was added) would have
        // missing tables that the prior CREATE-then-TRUNCATE flow could not
        // recover from. Drop CASCADE clears every dependent object so the
        // create script always runs from clean state.
        db.Database.ExecuteSqlRaw("DROP SCHEMA IF EXISTS test_authz CASCADE");
        db.Database.ExecuteSqlRaw("CREATE SCHEMA test_authz");
        db.Database.ExecuteSqlRaw(db.Database.GenerateCreateScript());

        var alice = new Owner
        {
            Name = "Alice",
            Books =
            {
                new OwnedBook { Title = "Alice's First Book" },
                new OwnedBook { Title = "Alice's Second Book" },
            },
        };
        var bob = new Owner
        {
            Name = "Bob",
            Books = { new OwnedBook { Title = "Bob's Only Book" } },
        };
        db.Owners.AddRange(alice, bob);

        db.Memos.AddRange(new Memo { Body = "memo-1" }, new Memo { Body = "memo-2" });
        db.RestrictedDocs.Add(new RestrictedDoc { Payload = "restricted-1" });
        db.MemberAreas.Add(new MemberArea { Name = "lounge" });

        db.SaveChanges();

        // PublicBook seed runs after the Owner/OwnedBook seed so the FKs
        // resolve. Alice's first PublicBook intentionally links to one of her
        // OwnedBooks to exercise the cascade-from-anonymous-to-gated path.
        var aliceFirstBook = db.OwnedBooks.OrderBy(b => b.Id).First(b => b.OwnerId == alice.Id);
        db.PublicBooks.AddRange(
            new PublicBook
            {
                OwnerId = alice.Id,
                Title = "Alice's Public Notice",
                LinkedOwnedBookId = aliceFirstBook.Id,
            },
            new PublicBook
            {
                OwnerId = alice.Id,
                Title = "Alice's Other Public Notice",
                LinkedOwnedBookId = null,
            },
            new PublicBook
            {
                OwnerId = bob.Id,
                Title = "Bob's Public Notice",
                LinkedOwnedBookId = null,
            }
        );

        db.SaveChanges();
    }
}
