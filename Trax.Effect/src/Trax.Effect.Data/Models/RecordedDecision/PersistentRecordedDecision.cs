using Microsoft.EntityFrameworkCore;
using BaseModel = Trax.Effect.Models.RecordedDecision.RecordedDecision;
using MetadataModel = Trax.Effect.Models.Metadata.Metadata;

namespace Trax.Effect.Data.Models.RecordedDecision;

/// <summary>
/// Provides EF Core configuration for <see cref="Trax.Effect.Models.RecordedDecision.RecordedDecision"/>,
/// the <c>trax.decision</c> table.
/// </summary>
public class PersistentRecordedDecision : BaseModel
{
    internal static void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BaseModel>(entity =>
        {
            entity.ToTable("decision", "trax");
            entity.HasKey(e => e.Id);

            // Deleted with its run, by the database, so every existing delete of metadata keeps
            // working without knowing this table exists.
            entity
                .HasOne<MetadataModel>()
                .WithMany()
                .HasForeignKey(e => e.MetadataId)
                .OnDelete(DeleteBehavior.Cascade);

            entity
                .HasIndex(e => new
                {
                    e.MetadataId,
                    e.QuestionKey,
                    e.Occurrence,
                })
                .IsUnique()
                .HasDatabaseName("uq_decision_run_question");

            entity.Property(e => e.Question).HasColumnType("jsonb");
            entity.Property(e => e.Answer).HasColumnType("jsonb");
            entity.Property(e => e.Shadows).HasColumnType("jsonb");
            entity.Property(e => e.Routes).HasColumnType("jsonb");
        });
    }
}
