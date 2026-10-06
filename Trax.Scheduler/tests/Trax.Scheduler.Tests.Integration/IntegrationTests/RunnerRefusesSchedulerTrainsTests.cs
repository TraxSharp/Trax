using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.DeadLetterCleanup;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;
using Trax.Scheduler.Trains.ManifestManager;
using Trax.Scheduler.Trains.MetadataCleanup;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A runner runs the host's trains, never the scheduler's own: the run path refuses them by any
/// name they go by, and a queued job whose row names one is refused and left Pending.
///
/// <para>Enforces <c>docs/adr/0006-a-runner-requires-an-authorization-posture.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0006-a-runner-requires-an-authorization-posture.md")]
[TestFixture]
public class RunnerRefusesSchedulerTrainsTests : TestSetup
{
    private TraxRequestHandler Handler =>
        ActivatorUtilities.CreateInstance<TraxRequestHandler>(Scope.ServiceProvider, JobRunner);

    private static IEnumerable<string> SchedulerTrainNames()
    {
        foreach (
            var type in new[]
            {
                typeof(IMetadataCleanupTrain),
                typeof(IDeadLetterCleanupTrain),
                typeof(IManifestManagerTrain),
                typeof(IJobDispatcherTrain),
                typeof(IJobRunnerTrain),
            }
        )
        {
            yield return type.FullName!;
            yield return type.Name;
        }
    }

    [TestCaseSource(nameof(SchedulerTrainNames))]
    public async Task RunTrainAsync_NamingASchedulerTrain_IsRefusedWithoutRunningIt(
        string trainName
    )
    {
        var response = await Handler.RunTrainAsync(
            new RemoteRunRequest(trainName, "{}", "ignored")
        );

        response.IsError.Should().BeTrue();
        response
            .ErrorMessage.Should()
            .Contain(
                "scheduler's own",
                "a runner runs only the host's trains (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );

        DataContext.Reset();
        (await DataContext.Metadatas.AsNoTracking().CountAsync())
            .Should()
            .Be(0, "the refused train never started");
    }

    [Test]
    public async Task RunTrainAsync_NamingAHostTrain_StillRuns()
    {
        var response = await Handler.RunTrainAsync(
            new RemoteRunRequest(
                typeof(Fakes.Trains.ISchedulerTestTrain).FullName!,
                """{"value":"runs"}""",
                typeof(Fakes.Trains.SchedulerTestInput).FullName!
            )
        );

        response.IsError.Should().BeFalse(response.ErrorMessage);
    }

    [Test]
    public async Task Run_PendingRowOfASchedulerTrain_IsRefusedAndTheRowStaysPending()
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IMetadataCleanupTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new MetadataCleanupRequest(),
            }
        );
        metadata.TrainState = TrainState.Pending;
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var act = async () =>
            await JobRunner.Run(new RunJobRequest(metadata.Id, new MetadataCleanupRequest()));

        await act.Should().ThrowAsync<TrainException>().WithMessage("*scheduler's own*");

        DataContext.Reset();
        var row = await DataContext.Metadatas.AsNoTracking().FirstAsync(x => x.Id == metadata.Id);
        row.TrainState.Should()
            .Be(
                TrainState.Pending,
                "a runner never runs the scheduler's own trains (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );
    }

    [TestCase(nameof(IMetadataCleanupTrain))]
    [TestCase(nameof(MetadataCleanupTrain))]
    public async Task Run_PendingRowNamingASchedulerTrainByAShortName_IsRefusedAndTheRowStaysPending(
        string name
    )
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new MetadataCleanupRequest(),
            }
        );
        metadata.TrainState = TrainState.Pending;
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var act = async () =>
            await JobRunner.Run(new RunJobRequest(metadata.Id, new MetadataCleanupRequest()));

        await act.Should()
            .ThrowAsync<TrainException>()
            .WithMessage(
                $"*names '{typeof(IMetadataCleanupTrain).FullName}', one of the scheduler's own*",
                "a short name resolves to the train it names, which is then refused"
            );
        DataContext.Reset();
        (await DataContext.Metadatas.AsNoTracking().FirstAsync(x => x.Id == metadata.Id))
            .TrainState.Should()
            .Be(TrainState.Pending);
    }

    [Test]
    public async Task Run_PendingRowOfTheManifestManager_IsRefusedAndTheRowStaysPending()
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(IManifestManagerTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = Unit.Default,
            }
        );
        metadata.TrainState = TrainState.Pending;
        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var act = async () => await JobRunner.Run(new RunJobRequest(metadata.Id, Unit.Default));

        await act.Should().ThrowAsync<TrainException>().WithMessage("*scheduler's own*");

        DataContext.Reset();
        var row = await DataContext.Metadatas.AsNoTracking().FirstAsync(x => x.Id == metadata.Id);
        row.TrainState.Should()
            .Be(
                TrainState.Pending,
                "a runner does not run the scheduler's own trains (see docs/adr/0006-a-runner-requires-an-authorization-posture.md)"
            );
    }
}
