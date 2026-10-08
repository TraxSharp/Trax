using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Samples.Recovery.Corpus;
using Trax.Samples.Recovery.E2E.Factories;
using Trax.Samples.Recovery.E2E.Utilities;
using Trax.Samples.Recovery.Faults;
using Trax.Samples.Recovery.Index;
using Trax.Samples.Recovery.Trains.Discover;
using Trax.Samples.Recovery.Trains.Ingest;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The index records seeded at startup, the partitions discovery lists from them, and the ingest of
/// one partition: deterministic, idempotent, and unsure where a title is only close to a corpus
/// paper's.
/// </summary>
[TestFixture]
public class IngestTests
{
    private static readonly IngestPartitionInput MergedPartition = new(
        IndexFixture.OpenAlex,
        "2025-01"
    );

    private static readonly IngestPartitionInput UnsurePartition = new(
        IndexFixture.Crossref,
        "2025-02"
    );

    // Discovery starts a machine per partition, and each queues an ingest of its partition. A test
    // here that runs the ingest itself, or arms its crash, waits for those to finish first.
    [SetUp]
    public Task LetThePartitionMachinesFinish() => PartitionMachines.WaitUntilSettledAsync();

    [Test]
    public async Task Seeding_Twice_AddsNoRecords()
    {
        // The host seeded the records when it started; seeding again finds every one there.
        var added = await IndexSeeder.SeedAsync(SharedRecoverySetup.Factory.Services);

        added.Should().Be(0);
        (await SeededRecords()).Should().HaveCount(IndexFixture.Records.Count);
    }

    [Test]
    public async Task Seeding_AddsBackOnlyTheRecordsThatAreMissing()
    {
        var removed = IndexFixture.Records[4];
        await using (var db = await Db())
            await db
                .SourceRecords.Where(r =>
                    r.Source == removed.Source && r.SourceId == removed.SourceId
                )
                .ExecuteDeleteAsync();

        var added = await IndexSeeder.SeedAsync(SharedRecoverySetup.Factory.Services);

        added.Should().Be(1);
        (await SeededRecords()).Should().HaveCount(IndexFixture.Records.Count);
    }

    [Test]
    public async Task ASecondHostStartingOnTheSameDatabase_SeedsNothingAndChangesNothing()
    {
        var before = await SeededRecords();

        await using (var second = new RecoveryApiFactory())
            // Building the host runs its startup, seeding included.
            _ = second.Services;

        var after = await SeededRecords();
        after.Should().BeEquivalentTo(before, o => o.WithStrictOrdering());
        after.Should().HaveCount(IndexFixture.Records.Count);
    }

    [Test]
    public async Task Discovery_ListsEverySourceAndMonth_InOrder_AndTheSameOnARerun()
    {
        var first = await Discover(new DiscoverPartitionsInput());
        var second = await Discover(new DiscoverPartitionsInput());

        var expected = IndexFixture
            .Sources.Order(StringComparer.Ordinal)
            .SelectMany(source =>
                IndexFixture.Months.Select(month => new SourcePartition(source, month, 3))
            );
        first.Partitions.Should().Equal(expected);
        second.Partitions.Should().Equal(first.Partitions);
    }

    [Test]
    public async Task Discovery_ForOneSource_ListsOnlyItsPartitions()
    {
        var found = await Discover(new DiscoverPartitionsInput { Source = IndexFixture.Crossref });

        found.Partitions.Should().HaveCount(IndexFixture.Months.Count);
        found.Partitions.Should().OnlyContain(p => p.Source == IndexFixture.Crossref);
    }

    [Test]
    public async Task Discovery_OfAnUnknownSource_IsRefused()
    {
        var act = () => Discover(new DiscoverPartitionsInput { Source = "Scopus" });

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*no source Scopus*");
    }

    [Test]
    public async Task Ingest_OfAPartitionHoldingACorpusPaper_MergesIt_AndCreatesTheRest()
    {
        var result = await Ingest(MergedPartition);

        result
            .Should()
            .Match<IngestPartitionResult>(r =>
                r.Works == 3
                && r.Merged == 1
                && r.Created == 2
                && r.NeedsReview == 0
                && !r.Unsure
                && r.Fingerprint.Length == 64
            );

        var rows = await Rows(MergedPartition);
        rows.Select(r => (r.SourceId, r.Resolution, r.ExistingWorkId))
            .Should()
            .Equal(
                ("W41001", Resolutions.Merged, "W30001"),
                ("W41002", Resolutions.Created, null),
                ("W41003", Resolutions.Created, null)
            );

        // Normalised from OpenAlex's shape: a bare DOI, the abstract rebuilt from its inverted index.
        rows[0].Doi.Should().Be("10.5555/trax.41001");
        rows[0].Abstract.Should().StartWith("Groundwater recharge through fractured chalk arrives");
        rows[0].References.Should().Equal("W30002", "W30003");
        rows[0].Year.Should().Be(2025);
    }

    [Test]
    public async Task Ingest_OfAPartitionWithACloseTitle_IsUnsure_AndHoldsThatWorkForReview()
    {
        var key = IndexFixture.PartitionKey(UnsurePartition.Source, UnsurePartition.Month);
        var askedBefore = SharedRecoverySetup.Factory.Decider.Asked(
            key,
            nameof(SameWorkAsExisting)
        );

        var result = await Ingest(UnsurePartition);

        result.Unsure.Should().BeTrue();
        result.NeedsReview.Should().Be(1);
        result.Created.Should().Be(2);
        result.Merged.Should().Be(0);
        SharedRecoverySetup
            .Factory.Decider.Asked(key, nameof(SameWorkAsExisting))
            .Should()
            .Be(askedBefore + 1);

        var review = (await Rows(UnsurePartition)).Single(r =>
            r.Resolution == Resolutions.NeedsReview
        );
        review
            .SourceId.Should()
            .Be("10.5555/TRAX.42004", "Crossref keys an item by its DOI as sent");
        review.Doi.Should().Be("10.5555/trax.42004");
        review.ExistingWorkId.Should().Be("W30028", "its title is close to the green roofs paper");

        // Normalised from Crossref's shape: the JATS tags stripped, authors as given and family name.
        review.Abstract.Should().NotContain("<jats:");
        review.Authors.Should().Equal("Eva Lund", "Clara Maier");
    }

    [Test]
    public async Task Ingest_OfAPartitionWithNothingClose_CreatesEveryWork()
    {
        var result = await Ingest(new IngestPartitionInput(IndexFixture.Crossref, "2025-03"));

        result.Created.Should().Be(3);
        result.Merged.Should().Be(0);
        result.NeedsReview.Should().Be(0);
        result.Unsure.Should().BeFalse();
    }

    [Test]
    public async Task Ingest_RunTwice_ReturnsTheSameOutput_AndLeavesTheSameRows()
    {
        var first = await Ingest(UnsurePartition);
        var rowsAfterFirst = await Rows(UnsurePartition);

        var second = await Ingest(UnsurePartition);
        var rowsAfterSecond = await Rows(UnsurePartition);

        second.Should().Be(first);
        rowsAfterSecond.Should().BeEquivalentTo(rowsAfterFirst, o => o.WithStrictOrdering());
        rowsAfterSecond.Should().HaveCount(3);
    }

    [Test]
    public async Task Ingest_OfEachPartition_HasItsOwnFingerprint()
    {
        var fingerprints = new List<string>();
        foreach (var source in IndexFixture.Sources)
        foreach (var month in IndexFixture.Months)
            fingerprints.Add((await Ingest(new IngestPartitionInput(source, month))).Fingerprint);

        fingerprints.Should().OnlyHaveUniqueItems().And.HaveCount(6);
    }

    [Test]
    public async Task Ingest_CrashedBeforeTheUpsert_WritesNothing_AndARerunWritesThePartition()
    {
        var partition = new IngestPartitionInput(IndexFixture.OpenAlex, "2025-03");
        await using (var db = await Db())
            await db
                .IngestedWorks.Where(w =>
                    w.Source == partition.Source && w.Month == partition.Month
                )
                .ExecuteDeleteAsync();
        Faults.Arm(IndexFixture.PartitionKey(partition.Source, partition.Month), CrashPoint.Ingest);

        var act = () => Ingest(partition);

        await act.Should().ThrowAsync<TimeoutException>().WithMessage("*crash injected*");
        (await Rows(partition)).Should().BeEmpty("the crash fires before the upsert writes");

        var result = await Ingest(partition);

        result.Created.Should().Be(3);
        (await Rows(partition)).Should().HaveCount(3);
    }

    [Test]
    public async Task Ingest_OfAMonthWithNoRecords_IsRefused()
    {
        var act = () => Ingest(new IngestPartitionInput(IndexFixture.OpenAlex, "1999-01"));

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*OpenAlex/1999-01*");
    }

    private static FaultInjector Faults =>
        SharedRecoverySetup.Factory.Services.GetRequiredService<FaultInjector>();

    private static async Task<IngestPartitionResult> Ingest(IngestPartitionInput input)
    {
        await using var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IIngestPartitionTrain>().Run(input);
    }

    private static async Task<DiscoveredPartitions> Discover(DiscoverPartitionsInput input)
    {
        await using var scope = SharedRecoverySetup.Factory.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDiscoverPartitionsTrain>()
            .Run(input);
    }

    private static Task<TopicMapDbContext> Db() =>
        SharedRecoverySetup
            .Factory.Services.GetRequiredService<IDbContextFactory<TopicMapDbContext>>()
            .CreateDbContextAsync();

    private static async Task<List<SourceRecord>> SeededRecords()
    {
        await using var db = await Db();
        return await db
            .SourceRecords.AsNoTracking()
            .OrderBy(r => r.Source)
            .ThenBy(r => r.SourceId)
            .ToListAsync();
    }

    private static async Task<List<IngestedWork>> Rows(IngestPartitionInput partition)
    {
        await using var db = await Db();
        return await db
            .IngestedWorks.AsNoTracking()
            .Where(w => w.Source == partition.Source && w.Month == partition.Month)
            .OrderBy(w => w.SourceId)
            .ToListAsync();
    }
}
