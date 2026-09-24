using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Tests.Data.Sqlite.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>
/// The raw dispatch SQL run against rows EF wrote. The SQL compares enum columns to labels, so
/// it only works if EF stores those labels; a string-matching test of the SQL cannot tell.
/// </summary>
public class SqliteDispatchSqlTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services.BuildServiceProvider();

    [Test]
    public async Task The_claim_finds_a_queued_confirmed_entry_written_by_EF()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var dialect = Scope.ServiceProvider.GetRequiredService<ISqlDialect>();

        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        var entry = WorkQueue.Create(
            new CreateWorkQueue { TrainName = "Sqlite.Claim", InputTypeName = "Sqlite.Input" }
        );
        await context.Track(entry);
        await context.SaveChanges(CancellationToken.None);

        var raw = await ((DbContext)context)
            .Database.SqlQueryRaw<string>(
                "SELECT CAST(status AS TEXT) AS \"Value\" FROM work_queue"
            )
            .ToListAsync();
        TestContext.Out.WriteLine($"stored status: {string.Join(",", raw)}");

        var claimed = await context
            .WorkQueues.FromSqlRaw(dialect.ClaimWorkQueueEntry(), entry.Id)
            .AsNoTracking()
            .FirstOrDefaultAsync();

        claimed
            .Should()
            .NotBeNull(
                $"the claim must find the entry; status is stored as {string.Join(",", raw)}"
            );
    }

    [Test]
    public async Task A_busy_subject_blocks_the_claim_and_the_candidate_load()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var dialect = Scope.ServiceProvider.GetRequiredService<ISqlDialect>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);

        // One entry for the subject is dispatched with its run still pending.
        var metadata = Trax.Effect.Models.Metadata.Metadata.Create(
            new Trax.Effect.Models.Metadata.DTOs.CreateMetadata
            {
                Name = "Sqlite.Subject",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        var running = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Sqlite.Subject",
                InputTypeName = "Sqlite.Input",
                SubjectKey = "customer-1",
            }
        );
        running.Status = Trax.Effect.Enums.WorkQueueStatus.Dispatched;
        running.MetadataId = metadata.Id;
        var sibling = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Sqlite.Subject",
                InputTypeName = "Sqlite.Input",
                SubjectKey = "customer-1",
            }
        );
        var other = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Sqlite.Subject",
                InputTypeName = "Sqlite.Input",
                SubjectKey = "customer-2",
            }
        );
        await context.Track(running);
        await context.Track(sibling);
        await context.Track(other);
        await context.SaveChanges(CancellationToken.None);

        (
            await context
                .WorkQueues.FromSqlRaw(dialect.ClaimWorkQueueEntry(), sibling.Id)
                .AsNoTracking()
                .FirstOrDefaultAsync()
        )
            .Should()
            .BeNull("the subject has a run in flight");

        var candidates = await context
            .WorkQueues.FromSqlRaw(dialect.LoadGroupFairQueuedJobs(), 100)
            .AsNoTracking()
            .Select(w => w.Id)
            .ToListAsync();

        candidates.Should().Contain(other.Id).And.NotContain(sibling.Id);
    }

    [Test]
    public async Task A_row_written_without_a_failure_class_reads_and_filters_as_unclassified()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        var externalId = Guid.NewGuid().ToString("N");

        // What a row written before the column existed looks like: the migration default.
        await ((DbContext)context).Database.ExecuteSqlRawAsync(
            "INSERT INTO metadata (name, external_id, train_state, start_time) "
                + "VALUES ('Sqlite.Legacy', {0}, 0, '2026-01-01 00:00:00')",
            externalId
        );

        var row = await context
            .Metadatas.AsNoTracking()
            .Where(m =>
                m.ExternalId == externalId
                && m.FailureClass == Trax.Core.Exceptions.FailureClass.Unclassified
            )
            .SingleOrDefaultAsync();

        row.Should().NotBeNull("the default has to be the value EF stores, not the label");
    }
}
