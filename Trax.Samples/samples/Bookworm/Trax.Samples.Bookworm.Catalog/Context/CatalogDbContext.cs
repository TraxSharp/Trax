using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.Bookworm.Catalog.Models.Authors;
using Trax.Samples.Bookworm.Catalog.Models.Books;

namespace Trax.Samples.Bookworm.Catalog.Context;

/// <summary>
/// The catalog domain's data context. Owns the <c>catalog</c> schema and the authoritative
/// <see cref="Book"/> / <see cref="Author"/> tables.
/// </summary>
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : DomainDataContext<CatalogDbContext>(options),
        ICatalogDbContext
{
    public DbSet<Author> Authors => Set<Author>();
    public DbSet<Book> Books => Set<Book>();

    protected override string Schema => CatalogSchema.Name;

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Author>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Name).IsRequired();
        });

        modelBuilder.Entity<Book>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Title).IsRequired();
            entity.HasIndex(e => e.Isbn).IsUnique();
            entity
                .HasOne(e => e.Author)
                .WithMany()
                .HasForeignKey(e => e.AuthorId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
