using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

namespace Trax.Samples.Recovery.Corpus;

/// <summary>
/// The topic map's own data, in the <c>topic_map</c> schema beside Trax's tables: the corpus it
/// reads and the pairs it writes.
/// </summary>
public class TopicMapDbContext(DbContextOptions<TopicMapDbContext> options)
    : DomainDataContext<TopicMapDbContext>(options),
        ITopicMapDbContext
{
    public DbSet<Work> Works => Set<Work>();

    public DbSet<TopicPair> TopicPairs => Set<TopicPair>();

    protected override string Schema => "topic_map";

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder
            .Entity<TopicPair>()
            .HasIndex(p => new
            {
                p.RunId,
                p.WorkA,
                p.WorkB,
            })
            .IsUnique();
}
