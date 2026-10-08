using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Utils;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A run a state machine's invoking state queued carries the instance that queued it from its work queue entry to
/// its metadata, and the scheduler never retries or dead-letters it, because it has no manifest: a failure goes to
/// the state's <c>OnFailed</c>, and the machine retries by entering the state again. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture]
public class InvokedRunIsNeverRetriedTests : TestSetup
{
    private const string Adr =
        "Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md";

    [Test]
    public async Task The_scheduler_never_retries_or_dead_letters_an_invoked_run()
    {
        var instance = Guid.NewGuid();
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(IFailingSchedulerTestTrain).FullName!,
                Input = JsonSerializer.Serialize(
                    new FailingSchedulerTestInput { FailureMessage = "invoked" },
                    TraxJsonSerializationOptions.ManifestProperties
                ),
                InputTypeName = typeof(FailingSchedulerTestInput).AssemblyQualifiedName,
                InvokedBy = new InvokedBy("ingest", instance, SnapshotOwnerKind.System),
            }
        );
        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        await Scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>().Run(Unit.Default);

        DataContext.Reset();
        var dispatched = await DataContext
            .WorkQueues.AsNoTracking()
            .Include(q => q.Metadata)
            .SingleAsync(q => q.Id == entry.Id);
        var run = dispatched.Metadata!;
        run.InvokingMachine.Should()
            .Be("ingest", $"dispatch carries the link to the run. See {Adr}");
        run.InvokingInstanceId.Should().Be(instance);
        run.InvokingOwnerKind.Should().Be(SnapshotOwnerKind.System);

        // Failed, by the train or here, well inside every retry window.
        await DataContext
            .Metadatas.Where(m => m.Id == run.Id)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(m => m.TrainState, TrainState.Failed)
                    .SetProperty(m => m.EndTime, DateTime.UtcNow)
            );

        var manager = Scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>();
        for (var cycle = 0; cycle < 3; cycle++)
            await manager.Run(Unit.Default);

        DataContext.Reset();
        (
            await DataContext
                .WorkQueues.AsNoTracking()
                .CountAsync(q => q.TrainName == typeof(IFailingSchedulerTestTrain).FullName)
        )
            .Should()
            .Be(1, $"the scheduler queues no retry of an invoked run. See {Adr}");
        (await DataContext.DeadLetters.AsNoTracking().CountAsync())
            .Should()
            .Be(0, "and never dead-letters one");
    }
}
