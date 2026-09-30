using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Trax.Effect.Data.Models.Metadata;

/// <summary>
/// EF Core mapping for <see cref="Effect.Models.Metadata.Metadata"/>: maps it to <c>trax.metadata</c>,
/// with its input, output and host labels stored as <c>jsonb</c> and restricting foreign keys to its parent metadata and its manifest. Infrastructure applied by the data context; not intended for direct use. Query the rows
/// through <c>IDataContext</c>.
/// </summary>
internal class PersistentMetadata : Effect.Models.Metadata.Metadata
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Effect.Models.Metadata.Metadata>(entity =>
        {
            entity.ToTable("metadata", "trax");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();

            entity.HasIndex(e => e.ManifestId);
            entity.HasIndex(e => new { e.Name, e.TrainState });
            // Built by Postgres migration 050; declared here so a schema created from the model has it.
            entity.HasIndex(e => e.ExternalId).HasDatabaseName("ix_metadata_external_id");

            entity
                .HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity
                .HasOne(x => x.Manifest)
                .WithMany(x => x.Metadatas)
                .HasForeignKey(e => e.ManifestId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure the conversion from string to jsonb for PostgreSQL
            // This handles the conversion between C# string properties and PostgreSQL jsonb columns
            entity
                .Property(e => e.Input)
                .HasConversion(
                    // Convert string to jsonb format for database storage
                    v => v,
                    // Convert jsonb back to string when reading from database
                    v => v
                )
                .HasColumnType("jsonb");

            entity
                .Property(e => e.Output)
                .HasConversion(
                    // Convert string to jsonb format for database storage
                    v => v,
                    // Convert jsonb back to string when reading from database
                    v => v
                )
                .HasColumnType("jsonb");

            entity.Property(e => e.HostLabels).HasConversion(v => v, v => v).HasColumnType("jsonb");
        });
    }
}
