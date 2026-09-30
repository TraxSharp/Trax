using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Tests.Data.Sqlite.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>
/// SQLite stores an enum as its integer, so a partial index whose predicate names the Postgres label
/// (<c>status = 'queued'</c>) covers no row EF ever writes: the unique one enforces nothing and the
/// others can never serve a query. Every partial index on an enum column compares integers.
/// </summary>
public class SqliteEnumPartialIndexTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services.BuildServiceProvider();

    private interface IQueuedTrain;

    [Test]
    public async Task A_second_queued_entry_for_one_manifest_is_refused()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = (IDataContext)
            await factory.CreateDbContextAsync(CancellationToken.None);

        var group = new ManifestGroup { Name = $"queued-{Guid.NewGuid():N}" };
        context.ManifestGroups.Add(group);
        await context.SaveChanges(CancellationToken.None);
        var manifest = Manifest.Create(new CreateManifest { Name = typeof(IQueuedTrain) });
        manifest.ManifestGroupId = group.Id;
        context.Manifests.Add(manifest);
        await context.SaveChanges(CancellationToken.None);

        await context.Track(Queued(manifest.Id));
        await context.SaveChanges(CancellationToken.None);

        await context.Track(Queued(manifest.Id));
        var second = () => context.SaveChanges(CancellationToken.None);

        await second
            .Should()
            .ThrowAsync<DbUpdateException>(
                "a manifest has at most one queued entry, which the dispatcher relies on"
            );
    }

    [Test]
    public async Task A_dispatched_entry_does_not_count_against_the_queued_one()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = (IDataContext)
            await factory.CreateDbContextAsync(CancellationToken.None);

        var group = new ManifestGroup { Name = $"dispatched-{Guid.NewGuid():N}" };
        context.ManifestGroups.Add(group);
        await context.SaveChanges(CancellationToken.None);
        var manifest = Manifest.Create(new CreateManifest { Name = typeof(IQueuedTrain) });
        manifest.ManifestGroupId = group.Id;
        context.Manifests.Add(manifest);
        await context.SaveChanges(CancellationToken.None);

        var dispatched = Queued(manifest.Id);
        dispatched.Status = WorkQueueStatus.Dispatched;
        await context.Track(dispatched);
        await context.Track(Queued(manifest.Id));

        var act = () => context.SaveChanges(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// <c>INDEXED BY</c> makes SQLite use the named index or fail the statement, and it can use a
    /// partial index only when the query's <c>WHERE</c> implies the index's. Each query here filters
    /// the way Trax's own queries do, by the enum's integer.
    /// </summary>
    [TestCase("ix_work_queue_status", "SELECT id FROM work_queue INDEXED BY {0} WHERE status = 0")]
    [TestCase(
        "ix_work_queue_status_priority",
        "SELECT id FROM work_queue INDEXED BY {0} WHERE status = 0 ORDER BY priority DESC"
    )]
    [TestCase(
        "ix_work_queue_unique_queued_manifest",
        "SELECT id FROM work_queue INDEXED BY {0} WHERE status = 0 AND manifest_id = 1"
    )]
    [TestCase(
        "ix_work_queue_scheduled_at",
        "SELECT id FROM work_queue INDEXED BY {0} WHERE status = 0 AND scheduled_at < '2030-01-01'"
    )]
    [TestCase(
        "ix_work_queue_manifest_id_status_queued",
        "SELECT id FROM work_queue INDEXED BY {0} WHERE status = 0 AND manifest_id = 1"
    )]
    [TestCase(
        "ix_metadata_train_state_start_time",
        "SELECT id FROM metadata INDEXED BY {0} WHERE train_state IN (0, 3) AND start_time < '2030-01-01'"
    )]
    [TestCase(
        "ix_metadata_manifest_id_train_state",
        "SELECT id FROM metadata INDEXED BY {0} WHERE train_state IN (0, 3) AND manifest_id = 1"
    )]
    [TestCase(
        "ix_metadata_active_capacity",
        "SELECT id FROM metadata INDEXED BY {0} WHERE train_state IN (0, 3)"
    )]
    [TestCase(
        "ix_metadata_cleanup",
        "SELECT id FROM metadata INDEXED BY {0} WHERE train_state IN (1, 2, 4) AND name = 'x'"
    )]
    [TestCase(
        "ix_metadata_manifest_failed",
        "SELECT id FROM metadata INDEXED BY {0} WHERE train_state = 2 AND manifest_id = 1"
    )]
    [TestCase(
        "dead_letter_status_idx",
        "SELECT id FROM dead_letter INDEXED BY {0} WHERE status = 0"
    )]
    public async Task An_integer_filter_can_use_the_partial_index(string index, string query)
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = (DbContext)await factory.CreateDbContextAsync(CancellationToken.None);
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = string.Format(query, index);
        var act = () => command.ExecuteReaderAsync();

        await act.Should()
            .NotThrowAsync(
                $"{index} must cover the rows EF writes, whose enum columns hold integers"
            );
    }

    private static WorkQueue Queued(long manifestId) =>
        WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(IQueuedTrain).FullName!,
                ManifestId = manifestId,
            }
        );
}
