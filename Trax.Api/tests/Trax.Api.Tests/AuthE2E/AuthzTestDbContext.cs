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

public class AuthzTestDbContext(DbContextOptions<AuthzTestDbContext> options) : DbContext(options)
{
    public DbSet<Owner> Owners { get; set; } = null!;
    public DbSet<OwnedBook> OwnedBooks { get; set; } = null!;
    public DbSet<Memo> Memos { get; set; } = null!;
    public DbSet<RestrictedDoc> RestrictedDocs { get; set; } = null!;
    public DbSet<MemberArea> MemberAreas { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("test_authz");

        modelBuilder.Entity<Owner>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasMany(e => e.Books).WithOne(b => b.Owner).HasForeignKey(b => b.OwnerId);
        });

        modelBuilder.Entity<OwnedBook>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.OwnerId);
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

        db.Database.ExecuteSqlRaw("CREATE SCHEMA IF NOT EXISTS test_authz");
        try
        {
            db.Database.ExecuteSqlRaw(db.Database.GenerateCreateScript());
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P07")
        {
            // Already exists.
        }

        db.Database.ExecuteSqlRaw("TRUNCATE TABLE test_authz.owned_books RESTART IDENTITY CASCADE");
        db.Database.ExecuteSqlRaw("TRUNCATE TABLE test_authz.owners RESTART IDENTITY CASCADE");
        db.Database.ExecuteSqlRaw("TRUNCATE TABLE test_authz.memos RESTART IDENTITY");
        db.Database.ExecuteSqlRaw("TRUNCATE TABLE test_authz.restricted_docs RESTART IDENTITY");
        db.Database.ExecuteSqlRaw("TRUNCATE TABLE test_authz.member_areas RESTART IDENTITY");

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
    }
}
