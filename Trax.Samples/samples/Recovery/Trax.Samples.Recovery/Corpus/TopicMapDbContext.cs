using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.Recovery.Index;

namespace Trax.Samples.Recovery.Corpus;

/// <summary>
/// The topic map's own data, in the <c>topic_map</c> schema beside Trax's tables: the corpus it
/// reads, the pairs it writes, the index records a partition is ingested from, and the works the
/// ingest writes.
/// </summary>
public class TopicMapDbContext(DbContextOptions<TopicMapDbContext> options)
    : DomainDataContext<TopicMapDbContext>(options),
        ITopicMapDbContext
{
    public DbSet<Work> Works => Set<Work>();

    public DbSet<TopicPair> TopicPairs => Set<TopicPair>();

    public DbSet<SourceRecord> SourceRecords => Set<SourceRecord>();

    public DbSet<IngestedWork> IngestedWorks => Set<IngestedWork>();

    protected override string Schema => "topic_map";

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<TopicPair>()
            .HasIndex(p => new
            {
                p.RunId,
                p.WorkA,
                p.WorkB,
            })
            .IsUnique();

        modelBuilder.Entity<SourceRecord>(record =>
        {
            record.HasKey(r => new { r.Source, r.SourceId });
            record.HasIndex(r => new { r.Source, r.Month });
            record.Property(r => r.Payload).HasColumnType("jsonb");
        });

        modelBuilder.Entity<IngestedWork>(work =>
        {
            work.HasKey(w => new { w.Source, w.SourceId });
            work.HasIndex(w => new { w.Source, w.Month });
        });
    }
}
