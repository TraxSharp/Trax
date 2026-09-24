using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.WorkQueuePromotion;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Tests.Data.Sqlite.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>
/// The second phase of a two-phase enqueue, and the stale-staged sweeps, run as the
/// <c>ExecuteUpdate</c> statements SQLite actually executes. There the status is an integer and
/// <c>created_at</c> is TEXT, so the stale cutoff is a string comparison; the in-memory provider
/// exercises neither.
///
/// <para>Enforces Trax.Docs/adr/0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md"
)]
public class SqliteWorkQueuePromotionTests : TestSetup
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IWorkQueuePromotion, WorkQueuePromotion>().BuildServiceProvider();

    private IWorkQueuePromotion Promotion =>
        Scope.ServiceProvider.GetRequiredService<IWorkQueuePromotion>();

    private IDataContextProviderFactory Factory =>
        Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

    [Test]
    public async Task PromoteAsync_confirms_a_staged_entry()
    {
        var id = await Stage();

        var promoted = await Promotion.PromoteAsync(id, CancellationToken.None);

        promoted.Should().BeTrue();
        (await Load(id)).ConfirmedAt.Should().NotBeNull();
    }

    [Test]
    public async Task PromoteAsync_leaves_an_entry_cancelled_while_its_hook_ran_cancelled()
    {
        var id = await Stage(status: WorkQueueStatus.Cancelled);

        var promoted = await Promotion.PromoteAsync(id, CancellationToken.None);

        promoted.Should().BeFalse("an operator cancelled it, and confirming it would revive it");
        (await Load(id)).ConfirmedAt.Should().BeNull();
    }

    [Test]
    public async Task CancelStaleAsync_cancels_only_staged_entries_older_than_the_window()
    {
        var stale = await Stage(age: TimeSpan.FromHours(1));
        var recent = await Stage();
        var backlog = await Stage(age: TimeSpan.FromHours(1), confirmed: true);

        var cancelled = await Promotion.CancelStaleAsync(Window, CancellationToken.None);

        cancelled.Should().BeGreaterThanOrEqualTo(1);
        (await Load(stale)).Status.Should().Be(WorkQueueStatus.Cancelled);
        (await StoredStatus(stale))
            .Should()
            .Be(
                ((int)WorkQueueStatus.Cancelled).ToString(),
                "SQLite stores the enum's integer, and ExecuteUpdate has to write the same"
            );
        (await Load(recent))
            .Status.Should()
            .Be(WorkQueueStatus.Queued, "an entry younger than the window may still be mid-hook");
        (await Load(backlog))
            .Status.Should()
            .Be(WorkQueueStatus.Queued, "a confirmed entry waiting its turn is not stranded");
    }

    [Test]
    public async Task PromoteStaleAsync_promotes_only_staged_entries_older_than_the_window()
    {
        var stale = await Stage(age: TimeSpan.FromHours(1));
        var recent = await Stage();
        var cancelled = await Stage(age: TimeSpan.FromHours(1), status: WorkQueueStatus.Cancelled);

        var promoted = await Promotion.PromoteStaleAsync(Window, CancellationToken.None);

        promoted.Should().BeGreaterThanOrEqualTo(1);
        var entry = await Load(stale);
        entry.Status.Should().Be(WorkQueueStatus.Queued);
        entry.ConfirmedAt.Should().NotBeNull();
        (await Load(recent)).ConfirmedAt.Should().BeNull();
        (await Load(cancelled))
            .ConfirmedAt.Should()
            .BeNull("an operator cancelled it, and confirming it would revive it");
    }

    [Test]
    public async Task CancelStaleAsync_compares_a_created_at_written_by_the_column_default()
    {
        // A row whose created_at came from the migration's datetime('now') default rather than
        // from EF, so the TEXT cutoff comparison has to hold across both formats.
        var staleExternalId = Guid.NewGuid().ToString("N");
        var recentExternalId = Guid.NewGuid().ToString("N");
        using (var context = await Factory.CreateDbContextAsync(CancellationToken.None))
        {
            var database = ((DbContext)context).Database;
            await database.ExecuteSqlRawAsync(
                "INSERT INTO work_queue (external_id, train_name, input_type_name, status, created_at) "
                    + "VALUES ({0}, 'Sqlite.Default', 'Sqlite.Input', {1}, datetime('now', '-1 hour'))",
                staleExternalId,
                (int)WorkQueueStatus.Queued
            );
            await database.ExecuteSqlRawAsync(
                "INSERT INTO work_queue (external_id, train_name, input_type_name, status) "
                    + "VALUES ({0}, 'Sqlite.Default', 'Sqlite.Input', {1})",
                recentExternalId,
                (int)WorkQueueStatus.Queued
            );
        }

        await Promotion.CancelStaleAsync(Window, CancellationToken.None);

        (await LoadByExternalId(staleExternalId)).Status.Should().Be(WorkQueueStatus.Cancelled);
        (await LoadByExternalId(recentExternalId)).Status.Should().Be(WorkQueueStatus.Queued);
    }

    private async Task<long> Stage(
        TimeSpan? age = null,
        WorkQueueStatus status = WorkQueueStatus.Queued,
        bool confirmed = false
    )
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "Sqlite.Staged",
                InputTypeName = "Sqlite.Input",
                DeferPromotion = true,
            }
        );
        entry.CreatedAt = DateTime.UtcNow - (age ?? TimeSpan.Zero);
        entry.Status = status;
        if (confirmed)
            entry.ConfirmedAt = entry.CreatedAt;

        using var context = await Factory.CreateDbContextAsync(CancellationToken.None);
        await context.Track(entry);
        await context.SaveChanges(CancellationToken.None);

        return entry.Id;
    }

    private async Task<WorkQueue> Load(long id)
    {
        using var context = await Factory.CreateDbContextAsync(CancellationToken.None);
        return await context.WorkQueues.AsNoTracking().SingleAsync(w => w.Id == id);
    }

    private async Task<WorkQueue> LoadByExternalId(string externalId)
    {
        using var context = await Factory.CreateDbContextAsync(CancellationToken.None);
        return await context.WorkQueues.AsNoTracking().SingleAsync(w => w.ExternalId == externalId);
    }

    private async Task<string> StoredStatus(long id)
    {
        using var context = await Factory.CreateDbContextAsync(CancellationToken.None);
        return await ((DbContext)context)
            .Database.SqlQueryRaw<string>(
                "SELECT CAST(status AS TEXT) AS \"Value\" FROM work_queue WHERE id = {0}",
                id
            )
            .SingleAsync();
    }
}
