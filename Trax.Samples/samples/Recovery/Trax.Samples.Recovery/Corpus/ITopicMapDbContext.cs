using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.Recovery.Index;

namespace Trax.Samples.Recovery.Corpus;

/// <summary>Companion interface for <see cref="TopicMapDbContext"/>.</summary>
public interface ITopicMapDbContext : IDomainDataContext
{
    DbSet<Work> Works { get; }

    DbSet<TopicPair> TopicPairs { get; }

    DbSet<SourceRecord> SourceRecords { get; }

    DbSet<IngestedWork> IngestedWorks { get; }
}
