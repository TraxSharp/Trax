using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Test entity exposed via <see cref="TraxQueryModelAttribute"/>. Lives in
/// the isolated <c>test_auth</c> schema so it can coexist with the sample
/// tables without stepping on them.
/// </summary>
[TraxQueryModel(Namespace = "library", Description = "Test books for E2E auth coverage.")]
[Table("book_records", Schema = "test_auth")]
public class BookRecord
{
    [Key]
    [Column("id")]
    public long Id { get; set; }

    [Column("title")]
    public string Title { get; set; } = "";

    [Column("author")]
    public string Author { get; set; } = "";

    [Column("rating")]
    public int Rating { get; set; }
}

public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<BookRecord> Books { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("test_auth");
        modelBuilder.Entity<BookRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.Author);
        });
    }

    /// <summary>
    /// Ensures the <c>test_auth</c> schema and <c>book_records</c> table exist
    /// and contains a deterministic fixture set. Idempotent and safe to call
    /// from every test's setup.
    /// </summary>
    public static void EnsureSeeded(string connectionString)
    {
        var opts = new DbContextOptionsBuilder<TestDbContext>().UseNpgsql(connectionString).Options;
        using var db = new TestDbContext(opts);

        db.Database.ExecuteSqlRaw("CREATE SCHEMA IF NOT EXISTS test_auth");
        try
        {
            db.Database.ExecuteSqlRaw(db.Database.GenerateCreateScript());
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P07")
        {
            // Table already exists — ignore.
        }

        // Clear and reseed every time so assertions are stable regardless of
        // how many previous test runs touched the table.
        db.Database.ExecuteSqlRaw("TRUNCATE TABLE test_auth.book_records RESTART IDENTITY");
        db.Books.AddRange(
            new BookRecord
            {
                Title = "The Mythical Man-Month",
                Author = "Brooks",
                Rating = 5,
            },
            new BookRecord
            {
                Title = "Structure and Interpretation of Computer Programs",
                Author = "Abelson",
                Rating = 5,
            },
            new BookRecord
            {
                Title = "The C Programming Language",
                Author = "Kernighan",
                Rating = 5,
            },
            new BookRecord
            {
                Title = "Design Patterns",
                Author = "Gamma",
                Rating = 4,
            },
            new BookRecord
            {
                Title = "Refactoring",
                Author = "Fowler",
                Rating = 4,
            },
            new BookRecord
            {
                Title = "Clean Code",
                Author = "Martin",
                Rating = 3,
            },
            new BookRecord
            {
                Title = "Working Effectively with Legacy Code",
                Author = "Feathers",
                Rating = 4,
            },
            new BookRecord
            {
                Title = "Domain-Driven Design",
                Author = "Evans",
                Rating = 4,
            }
        );
        db.SaveChanges();
    }
}
