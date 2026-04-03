using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Tests.Sqlite.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Sqlite.Integration.Fixtures;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Sqlite.Integration.IntegrationTests;

[TestFixture]
public class SqliteTrainExecutionTests : TestSetup
{
    #region TrainBus

    [Test]
    public async Task TrainBus_RunsSuccessfulTrain_MetadataCompleted()
    {
        await TrainBus.RunAsync<Unit>(
            new SchedulerTestInput { Value = "hello" },
            CancellationToken.None
        );

        DataContext.Reset();

        var metadatas = await DataContext
            .Metadatas.Where(m => m.Name == typeof(ISchedulerTestTrain).FullName)
            .ToListAsync();

        metadatas.Should().ContainSingle();
        metadatas[0].TrainState.Should().Be(TrainState.Completed);
    }

    [Test]
    public async Task TrainBus_RunsFailingTrain_MetadataFailed()
    {
        var act = () =>
            TrainBus.RunAsync<Unit>(
                new FailingSchedulerTestInput { FailureMessage = "boom" },
                CancellationToken.None
            );

        await act.Should().ThrowAsync<Exception>();

        DataContext.Reset();

        var metadatas = await DataContext
            .Metadatas.Where(m => m.Name == typeof(IFailingSchedulerTestTrain).FullName)
            .ToListAsync();

        metadatas.Should().ContainSingle();
        metadatas[0].TrainState.Should().Be(TrainState.Failed);
        metadatas[0].FailureReason.Should().Contain("boom");
    }

    [Test]
    public async Task TrainBus_PersistsInput_InMetadata()
    {
        await TrainBus.RunAsync<Unit>(
            new SchedulerTestInput { Value = "persisted-value" },
            CancellationToken.None
        );

        DataContext.Reset();

        var metadata = await DataContext
            .Metadatas.Where(m => m.Name == typeof(ISchedulerTestTrain).FullName)
            .SingleAsync();

        metadata.Input.Should().NotBeNullOrEmpty();
        metadata.Input.Should().Contain("persisted-value");
    }

    [Test]
    public async Task TrainBus_MultipleTrains_AllPersisted()
    {
        for (var i = 0; i < 3; i++)
        {
            await TrainBus.RunAsync<Unit>(
                new SchedulerTestInput { Value = $"train-{i}" },
                CancellationToken.None
            );
        }

        DataContext.Reset();

        var metadatas = await DataContext
            .Metadatas.Where(m => m.Name == typeof(ISchedulerTestTrain).FullName)
            .ToListAsync();

        metadatas.Should().HaveCount(3);
        metadatas.Should().OnlyContain(m => m.TrainState == TrainState.Completed);
    }

    #endregion

    #region JobRunner

    [Test]
    public async Task JobRunner_ExecutesFromMetadataId_Completed()
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ISchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "job-runner-test" },
            }
        );

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var input = new SchedulerTestInput { Value = "job-runner-test" };
        var request = new RunJobRequest(metadata.Id, input);
        await JobRunner.Run(request, CancellationToken.None);

        DataContext.Reset();

        var updated = await DataContext.Metadatas.FirstAsync(m => m.Id == metadata.Id);

        updated.TrainState.Should().Be(TrainState.Completed);
    }

    #endregion
}
