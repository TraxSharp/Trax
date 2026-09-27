using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.WorkQueuePromotion;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// The stale-staged sweep as the statements Postgres executes, where the status is the
/// <c>trax.work_queue_status</c> enum and the delete is one <c>ExecuteDelete</c>.
///
/// <para>Enforces docs/adr/0007-cancelled-staged-entries-are-deleted-after-a-retention.md.</para>
/// </summary>
[Property("adr", "docs/adr/0007-cancelled-staged-entries-are-deleted-after-a-retention.md")]
[NonParallelizable]
public class PostgresWorkQueuePromotionTests : TestSetup
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private IWorkQueuePromotion Promotion => new WorkQueuePromotion(DataContextFactory);

    [Test]
    public async Task CancelStaleAsync_deletes_what_an_earlier_pass_cancelled_once_past_the_retention()
    {
        // Retention is 30 days from creation. Rows are aged rather than a clock advanced: the
        // sweep reads DateTime.UtcNow, and created_at is what it measures from.
        var run = Guid.NewGuid().ToString("N");
        await StageMany($"Postgres.Expired.{run}", 1000, age: TimeSpan.FromDays(31));
        await StageMany($"Postgres.Fresh.{run}", 1000, age: TimeSpan.FromHours(1));
        await StageMany(
            $"Postgres.OperatorCancelled.{run}",
            1,
            age: TimeSpan.FromDays(31),
            status: WorkQueueStatus.Cancelled,
            confirmed: true
        );

        var firstPass = await Promotion.CancelStaleAsync(Window, CancellationToken.None);

        firstPass.Should().BeGreaterThanOrEqualTo(2000);
        (await CountByStatus($"Postgres.Expired.{run}"))
            .Should()
            .Equal(
                new Dictionary<WorkQueueStatus, int> { [WorkQueueStatus.Cancelled] = 1000 },
                "an entry is deleted only on a pass after the one that cancelled it, so a sweep "
                    + "that ran late does not cancel and delete in one step"
            );

        await Promotion.CancelStaleAsync(Window, CancellationToken.None);

        (await CountByStatus($"Postgres.Expired.{run}"))
            .Should()
            .BeEmpty(
                "a cancelled staged entry past the retention is deleted by the next sweep "
                    + "(docs/adr/0007-cancelled-staged-entries-are-deleted-after-a-retention.md)"
            );
        (await CountByStatus($"Postgres.Fresh.{run}"))
            .Should()
            .Equal(
                new Dictionary<WorkQueueStatus, int> { [WorkQueueStatus.Cancelled] = 1000 },
                "a cancelled staged entry inside the retention is the record of an enqueue that "
                    + "vanished, kept so its side-effect can be reconciled"
            );
        (await CountByStatus($"Postgres.OperatorCancelled.{run}"))
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
        using var context = await DataContextFactory.CreateDbContextAsync(CancellationToken.None);

        for (var i = 0; i < count; i++)
        {
            var entry = WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = trainName,
                    InputTypeName = "Postgres.Input",
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
        using var context = await DataContextFactory.CreateDbContextAsync(CancellationToken.None);

        var statuses = await context
            .WorkQueues.AsNoTracking()
            .Where(w => w.TrainName == trainName)
            .Select(w => w.Status)
            .ToListAsync();

        return statuses.GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
    }
}
