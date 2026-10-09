using Microsoft.EntityFrameworkCore;
using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Corpus;
using Trax.Samples.Recovery.Index;

namespace Trax.Samples.Recovery.Trains.Ingest.Junctions;

/// <summary>
/// Reads the partition's records as the index sent them. The seeded <c>source_records</c> table
/// stands in for the index, so nothing goes over the network and every run reads the same records.
/// </summary>
public class FetchPartition(ITopicMapDbContext db, DemoPace pace)
    : EffectJunction<IngestPartitionInput, FetchedPartition>
{
    public override async Task<FetchedPartition> Run(IngestPartitionInput input)
    {
        if (!IndexFixture.Sources.Contains(input.Source))
            throw new ArgumentException(
                $"There is no source {input.Source}; the sources are "
                    + $"{string.Join(", ", IndexFixture.Sources)}.",
                nameof(input)
            );

        await Task.Delay(pace.StepDelay, CancellationToken);

        var records = await db
            .SourceRecords.AsNoTracking()
            .Where(r => r.Source == input.Source && r.Month == input.Month)
            .OrderBy(r => r.SourceId)
            .Select(r => new FetchedRecord(r.SourceId, r.Payload))
            .ToListAsync(CancellationToken);

        if (records.Count == 0)
            throw new ArgumentException(
                $"{IndexFixture.PartitionKey(input.Source, input.Month)} has no records.",
                nameof(input)
            );

        return new FetchedPartition(input.Source, input.Month, records);
    }
}
