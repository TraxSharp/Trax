using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.WorkQueuePromotion;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Tests.Data.InMemory.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// The second phase of a two-phase enqueue, and the sweep that resolves entries a crash left
/// unconfirmed, on the in-memory provider. That provider cannot translate ExecuteUpdate, and a
/// deferring train that threw after its hook had already run would be the result.
///
/// <para>Enforces Trax.Docs/adr/0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md"
)]
public class WorkQueuePromotionTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IWorkQueuePromotion, WorkQueuePromotion>().BuildServiceProvider();

    private IWorkQueuePromotion Promotion =>
        Scope.ServiceProvider.GetRequiredService<IWorkQueuePromotion>();

    [Test]
    public async Task Promoting_a_staged_entry_confirms_it()
    {
        var id = await Stage();

        var promoted = await Promotion.PromoteAsync(id, CancellationToken.None);

        promoted.Should().BeTrue();
        (await Load(id)).ConfirmedAt.Should().NotBeNull();
    }

    [Test]
    public async Task Promoting_an_entry_cancelled_while_its_hook_ran_leaves_it_cancelled()
    {
        var id = await Stage(status: WorkQueueStatus.Cancelled);

        var promoted = await Promotion.PromoteAsync(id, CancellationToken.None);

        promoted.Should().BeFalse("an operator cancelled it, and confirming it would revive it");
        (await Load(id)).ConfirmedAt.Should().BeNull();
    }

    [Test]
    public async Task The_stale_sweep_cancels_old_staged_entries_and_leaves_recent_ones()
    {
        var stale = await Stage(age: TimeSpan.FromHours(1));
        var recent = await Stage();

        var cancelled = await Promotion.CancelStaleAsync(
            TimeSpan.FromMinutes(10),
            CancellationToken.None
        );

        cancelled.Should().BeGreaterThanOrEqualTo(1);
        (await Load(stale)).Status.Should().Be(WorkQueueStatus.Cancelled);
        (await Load(recent))
            .Status.Should()
            .Be(WorkQueueStatus.Queued, "an entry younger than the window may still be mid-hook");
    }

    [Test]
    public async Task The_opt_in_stale_sweep_promotes_old_staged_entries()
    {
        var stale = await Stage(age: TimeSpan.FromHours(1));

        await Promotion.PromoteStaleAsync(TimeSpan.FromMinutes(10), CancellationToken.None);

        var entry = await Load(stale);
        entry.Status.Should().Be(WorkQueueStatus.Queued);
        entry.ConfirmedAt.Should().NotBeNull();
    }

    [Test]
    public async Task The_stale_sweeps_leave_an_old_confirmed_backlog_alone()
    {
        var backlog = await Stage(age: TimeSpan.FromHours(1), confirmed: true);
        var confirmedAt = (await Load(backlog)).ConfirmedAt;

        await Promotion.CancelStaleAsync(TimeSpan.FromMinutes(10), CancellationToken.None);
        await Promotion.PromoteStaleAsync(TimeSpan.FromMinutes(10), CancellationToken.None);

        var entry = await Load(backlog);
        entry
            .Status.Should()
            .Be(
                WorkQueueStatus.Queued,
                "a confirmed entry waiting its turn is a backlog, not a stranded stage"
            );
        entry.ConfirmedAt.Should().Be(confirmedAt);
    }

    [Test]
    public async Task The_opt_in_stale_sweep_does_not_revive_a_cancelled_staged_entry()
    {
        var cancelled = await Stage(age: TimeSpan.FromHours(1), status: WorkQueueStatus.Cancelled);

        await Promotion.PromoteStaleAsync(TimeSpan.FromMinutes(10), CancellationToken.None);

        var entry = await Load(cancelled);
        entry.Status.Should().Be(WorkQueueStatus.Cancelled);
        entry
            .ConfirmedAt.Should()
            .BeNull("an operator cancelled it, and confirming it would revive it");
    }

    [Test]
    public async Task The_stale_sweep_deletes_what_an_earlier_pass_cancelled_once_past_the_retention()
    {
        // Retention is 30 days from creation. Rows are aged rather than a clock advanced: the
        // sweep reads DateTime.UtcNow, and created_at is what it measures from.
        var run = Guid.NewGuid().ToString("N");
        await StageMany($"Staged.Expired.{run}", 50, age: TimeSpan.FromDays(31));
        await StageMany($"Staged.Fresh.{run}", 50, age: TimeSpan.FromHours(1));
        await StageMany(
            $"Staged.OperatorCancelled.{run}",
            1,
            age: TimeSpan.FromDays(31),
            status: WorkQueueStatus.Cancelled,
            confirmed: true
        );

        var firstPass = await Promotion.CancelStaleAsync(
            TimeSpan.FromMinutes(10),
            CancellationToken.None
        );

        firstPass.Should().BeGreaterThanOrEqualTo(100);
        (await CountByStatus($"Staged.Expired.{run}"))
            .Should()
            .Equal(
                new Dictionary<WorkQueueStatus, int> { [WorkQueueStatus.Cancelled] = 50 },
                "an entry is deleted only on a pass after the one that cancelled it, so a sweep "
                    + "that ran late does not cancel and delete in one step"
            );

        await Promotion.CancelStaleAsync(TimeSpan.FromMinutes(10), CancellationToken.None);

        (await CountByStatus($"Staged.Expired.{run}"))
            .Should()
            .BeEmpty("a cancelled staged entry past the retention is deleted by the next sweep");
        (await CountByStatus($"Staged.Fresh.{run}"))
            .Should()
            .Equal(
                new Dictionary<WorkQueueStatus, int> { [WorkQueueStatus.Cancelled] = 50 },
                "a cancelled staged entry inside the retention is the record of an enqueue that "
                    + "vanished, kept so its side-effect can be reconciled"
            );
        (await CountByStatus($"Staged.OperatorCancelled.{run}"))
            .Should()
            .Equal(
                new Dictionary<WorkQueueStatus, int> { [WorkQueueStatus.Cancelled] = 1 },
                "a confirmed entry an operator cancelled was never staged, and is left alone"
            );
    }

    private async Task StageMany(
        string trainName,
        int count,
        TimeSpan age,
        WorkQueueStatus status = WorkQueueStatus.Queued,
        bool confirmed = false
    )
    {
        using var context = await Factory.CreateDbContextAsync(CancellationToken.None);

        for (var i = 0; i < count; i++)
        {
            var entry = WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = trainName,
                    InputTypeName = "Staged.Input",
                    DeferPromotion = true,
                }
            );
            entry.CreatedAt = DateTime.UtcNow - age;
            entry.Status = status;
            if (confirmed)
                entry.ConfirmedAt = entry.CreatedAt;
            await context.Track(entry);
        }

        await context.SaveChanges(CancellationToken.None);
    }

    private async Task<Dictionary<WorkQueueStatus, int>> CountByStatus(string trainName)
    {
        using var context = await Factory.CreateDbContextAsync(CancellationToken.None);

        var statuses = await context
            .WorkQueues.AsNoTracking()
            .Where(w => w.TrainName == trainName)
            .Select(w => w.Status)
            .ToListAsync();

        return statuses.GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
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
                TrainName = "Staged.Train",
                InputTypeName = "Staged.Input",
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

    private IDataContextProviderFactory Factory =>
        Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
}
