using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

namespace Trax.Samples.Recovery.Corpus;

/// <summary>Companion interface for <see cref="TopicMapDbContext"/>.</summary>
public interface ITopicMapDbContext : IDomainDataContext
{
    DbSet<Work> Works { get; }

    DbSet<TopicPair> TopicPairs { get; }
}
