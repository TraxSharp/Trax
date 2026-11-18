using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests.AuthE2E;

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
