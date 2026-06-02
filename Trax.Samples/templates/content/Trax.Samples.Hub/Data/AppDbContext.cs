using Microsoft.EntityFrameworkCore;
using Trax.Samples.Hub.Data.Models;

namespace Trax.Samples.Hub.Data;

/// <summary>
/// The application's domain data context. It is a plain EF context, separate from Trax's own effect
/// metadata store, registered alongside the Trax effect layer.
/// </summary>
/// <remarks>
/// This template ships a single domain context for one schema. For a multi-domain app, give each
/// domain its own project, schema, and context, and resolve relationships that cross schema
/// boundaries through cross-schema GraphQL data loaders in a dedicated project. The Bookworm sample
/// (and the Trax.Samples.Shared.Data library's SampleDataContext base) shows that full pattern.
/// </remarks>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options),
        IAppDbContext
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // One project : one schema : one context. The default schema applies on PostgreSQL;
        // schema-less providers (the in-memory provider used here, SQLite) ignore it.
        if (Database.ProviderName?.Contains("Npgsql", StringComparison.Ordinal) ?? false)
            modelBuilder.HasDefaultSchema(AppSchema.Name);

        modelBuilder.Entity<Note>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Text).IsRequired();
        });
    }
}
