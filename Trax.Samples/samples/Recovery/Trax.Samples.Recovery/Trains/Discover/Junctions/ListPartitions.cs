using Microsoft.EntityFrameworkCore;
using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Corpus;
using Trax.Samples.Recovery.Index;

namespace Trax.Samples.Recovery.Trains.Discover.Junctions;

/// <summary>Reads which source and month pairs the seeded index records fall into.</summary>
public class ListPartitions(ITopicMapDbContext db, DemoPace pace)
    : EffectJunction<DiscoverPartitionsInput, DiscoveredPartitions>
{
    public override async Task<DiscoveredPartitions> Run(DiscoverPartitionsInput input)
    {
        if (input.Source is { } source && !IndexFixture.Sources.Contains(source))
            throw new ArgumentException(
                $"There is no source {source}; the sources are "
                    + $"{string.Join(", ", IndexFixture.Sources)}.",
                nameof(input)
            );

        await Task.Delay(pace.StepDelay, CancellationToken);

        var partitions = await db
            .SourceRecords.AsNoTracking()
            .Where(r => input.Source == null || r.Source == input.Source)
            .GroupBy(r => new { r.Source, r.Month })
            .Select(g => new
            {
                g.Key.Source,
                g.Key.Month,
                Records = g.Count(),
            })
            .ToListAsync(CancellationToken);

        return new DiscoveredPartitions(
            partitions
                .OrderBy(p => p.Source, StringComparer.Ordinal)
                .ThenBy(p => p.Month, StringComparer.Ordinal)
                .Select(p => new SourcePartition(p.Source, p.Month, p.Records))
                .ToList()
        );
    }
}
