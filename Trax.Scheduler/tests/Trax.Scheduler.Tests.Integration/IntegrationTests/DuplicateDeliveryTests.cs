using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A job can reach the JobRunner more than once: SQS delivers at least once, a Lambda or HTTP
/// dispatch can be retried after the first attempt was accepted, and a local job can be claimed
/// again. The run's row decides which delivery runs it. The others complete without running the
/// train and without recording anything, so the delivery is acknowledged rather than retried or
/// counted as a failure.
/// </summary>
[TestFixture]
public class DuplicateDeliveryTests : TestSetup
{
    [Test]
    [Repeat(10)]
    public async Task Two_concurrent_deliveries_of_one_pending_run_run_the_train_once()
    {
        var key = Guid.NewGuid().ToString("N");
        var input = new DeliveryProbeInput { Key = key };
        var metadata = await CreatePendingRun(input);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DeliveryProbeTrain.Gates[key] = gate;

        try
        {
            var first = RunInOwnScope(new RunJobRequest(metadata.Id, input));
            var second = RunInOwnScope(new RunJobRequest(metadata.Id, input));

            // Whichever delivery lost does not wait for the train; the winner is held open until
            // the loser has finished, so the loser always meets a row that is not Pending.
            var loser = await Task.WhenAny(first, second).WaitAsync(TimeSpan.FromSeconds(30));
            await loser;
            gate.SetResult();

            var both = async () => await Task.WhenAll(first, second);
            await both.Should().NotThrowAsync("the losing delivery is acknowledged, not failed");

            DeliveryProbeTrain.Runs[key].Should().Be(1, "only one delivery owns the run");

            DataContext.Reset();
            var row = await DataContext
                .Metadatas.AsNoTracking()
                .SingleAsync(m => m.Id == metadata.Id);
            row.TrainState.Should().Be(TrainState.Completed);

            var failed = await DataContext
                .Metadatas.AsNoTracking()
                .Where(m => m.TrainState == TrainState.Failed)
                .Select(m => m.Name)
                .ToListAsync();
            failed.Should().BeEmpty("a duplicate delivery records no failure, not even its own");
        }
        finally
        {
            DeliveryProbeTrain.Forget(key);
        }
    }

    [Test]
    [TestCase(TrainState.InProgress)]
    [TestCase(TrainState.Completed)]
    [TestCase(TrainState.Failed)]
    [TestCase(TrainState.Cancelled)]
    public async Task A_delivery_of_a_run_that_is_no_longer_pending_completes_without_running_it(
        TrainState state
    )
    {
        var key = Guid.NewGuid().ToString("N");
        var input = new DeliveryProbeInput { Key = key };
        var metadata = await CreatePendingRun(input);
        await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.TrainState, state));
        DataContext.Reset();

        try
        {
            var act = async () => await JobRunner.Run(new RunJobRequest(metadata.Id, input));

            await act.Should().NotThrowAsync("another delivery owns, or owned, this run");
            DeliveryProbeTrain.Runs.ContainsKey(key).Should().BeFalse();

            DataContext.Reset();
            var row = await DataContext
                .Metadatas.AsNoTracking()
                .SingleAsync(m => m.Id == metadata.Id);
            row.TrainState.Should().Be(state, "nothing is recorded on a run another delivery owns");

            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .SingleAsync(m => m.Id == metadata.ManifestId);
            manifest.LastSuccessfulRun.Should().BeNull("this delivery did not run anything");
        }
        finally
        {
            DeliveryProbeTrain.Forget(key);
        }
    }

    [Test]
    public async Task A_pending_run_flagged_for_cancellation_is_recorded_cancelled_and_not_run()
    {
        // This host has no junction progress provider, so nothing but the job runner reads the
        // flag the batch, manifest and group cancels set on a Pending run.
        var key = Guid.NewGuid().ToString("N");
        var input = new DeliveryProbeInput { Key = key };
        var metadata = await CreatePendingRun(input);
        await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.CancellationRequested, true));
        DataContext.Reset();

        try
        {
            var act = async () => await JobRunner.Run(new RunJobRequest(metadata.Id, input));

            await act.Should().NotThrowAsync("the delivery is settled, not failed");
            DeliveryProbeTrain
                .Runs.ContainsKey(key)
                .Should()
                .BeFalse("a run cancelled before it started never enters the train");

            DataContext.Reset();
            var row = await DataContext
                .Metadatas.AsNoTracking()
                .SingleAsync(m => m.Id == metadata.Id);
            row.TrainState.Should().Be(TrainState.Cancelled);
            row.EndTime.Should().NotBeNull();

            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .SingleAsync(m => m.Id == metadata.ManifestId);
            manifest.LastSuccessfulRun.Should().BeNull("nothing ran");
        }
        finally
        {
            DeliveryProbeTrain.Forget(key);
        }
    }

    private async Task RunInOwnScope(RunJobRequest request)
    {
        // Yield first so both deliveries are in flight before either reaches the database.
        await Task.Yield();
        using var scope = Scope
            .ServiceProvider.GetRequiredService<IServiceScopeFactory>()
            .CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IJobRunnerTrain>();
        await runner.Run(request);
    }

    private async Task<Metadata> CreatePendingRun(DeliveryProbeInput input)
    {
        var group = await CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeliveryProbeTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
                Properties = input,
            }
        );
        manifest.ManifestGroupId = group.Id;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(DeliveryProbeTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ManifestId = manifest.Id,
            }
        );
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return metadata;
    }
}
