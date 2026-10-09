using Microsoft.EntityFrameworkCore;
using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Corpus;
using Trax.Samples.Recovery.Faults;
using Trax.Samples.Recovery.Index;

namespace Trax.Samples.Recovery.Trains.Ingest.Junctions;

/// <summary>
/// Writes the partition's works in one transaction, keyed by source and source id: a work already
/// there is overwritten with the same values, so running the partition again changes nothing. The
/// only step of the train that writes, and the step the page can crash, before it writes.
/// </summary>
public class UpsertWorks(
    IDbContextFactory<TopicMapDbContext> contexts,
    FaultInjector faults,
    DemoPace pace
) : EffectJunction<ResolvedPartition, UpsertedPartition>
{
    public override async Task<UpsertedPartition> Run(ResolvedPartition partition)
    {
        await Task.Delay(pace.StepDelay, CancellationToken);

        var key = IndexFixture.PartitionKey(partition.Source, partition.Month);
        if (faults.TryFire(key, CrashPoint.Ingest))
            throw new TimeoutException(
                "The works table did not answer in time (crash injected by the demo)."
            );

        await using var db = await contexts.CreateDbContextAsync(CancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken);

        foreach (var (work, resolution, existing) in partition.Works)
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO topic_map.ingested_works
                    ("Source", "SourceId", "Month", "Doi", "Title", "Abstract", "Year", "Authors",
                     "References", "ContentHash", "Resolution", "ExistingWorkId")
                VALUES ({partition.Source}, {work.SourceId}, {partition.Month}, {work.Doi},
                        {work.Title}, {work.Abstract}, {work.Year}, {work.Authors.ToArray()},
                        {work.References.ToArray()}, {work.ContentHash}, {resolution}, {existing})
                ON CONFLICT ("Source", "SourceId") DO UPDATE SET
                    "Month" = excluded."Month", "Doi" = excluded."Doi", "Title" = excluded."Title",
                    "Abstract" = excluded."Abstract", "Year" = excluded."Year",
                    "Authors" = excluded."Authors", "References" = excluded."References",
                    "ContentHash" = excluded."ContentHash", "Resolution" = excluded."Resolution",
                    "ExistingWorkId" = excluded."ExistingWorkId"
                """,
                CancellationToken
            );

        await transaction.CommitAsync(CancellationToken);

        return new UpsertedPartition(
            partition.Source,
            partition.Month,
            partition.Unsure,
            partition
                .Works.Select(w => new UpsertedWork(
                    w.Work.SourceId,
                    w.Work.ContentHash,
                    w.Resolution,
                    w.ExistingWorkId
                ))
                .ToList()
        );
    }
}
