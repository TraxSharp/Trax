using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Tests.Sqlite.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Sqlite.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Sqlite.Integration.IntegrationTests;

[TestFixture]
public class SqliteCleanupTests : TestSetup
{
    #region ExecuteDeleteAsync

    [Test]
    public async Task ExecuteDeleteAsync_SingleMetadata_DeletesSuccessfully()
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ISchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "delete-me" },
            }
        );

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var deleted = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .ExecuteDeleteAsync();

        deleted.Should().Be(1);

        var remaining = await DataContext.Metadatas.CountAsync();
        remaining.Should().Be(0);
    }

    [Test]
    public async Task ExecuteDeleteAsync_MultipleMetadatas_DeletesFiltered()
    {
        for (var i = 0; i < 5; i++)
        {
            var m = Metadata.Create(
                new CreateMetadata
                {
                    Name = typeof(ISchedulerTestTrain).FullName!,
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = new SchedulerTestInput { Value = $"item-{i}" },
                }
            );
            m.TrainState = i < 3 ? TrainState.Completed : TrainState.Failed;
            await DataContext.Track(m);
        }

        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var deleted = await DataContext
            .Metadatas.Where(m => m.TrainState == TrainState.Completed)
            .ExecuteDeleteAsync();

        deleted.Should().Be(3);

        var remaining = await DataContext.Metadatas.ToListAsync();
        remaining.Should().HaveCount(2);
        remaining.Should().OnlyContain(m => m.TrainState == TrainState.Failed);
    }

    [Test]
    public async Task ExecuteDeleteAsync_NoMatches_ReturnsZero()
    {
        var deleted = await DataContext
            .Metadatas.Where(m => m.Name == "nonexistent")
            .ExecuteDeleteAsync();

        deleted.Should().Be(0);
    }

    #endregion

    #region ExecuteUpdateAsync

    [Test]
    public async Task ExecuteUpdateAsync_UpdatesTrainState()
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ISchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "update-me" },
            }
        );

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(m => m.TrainState, TrainState.Failed)
                    .SetProperty(m => m.FailureReason, "test failure")
            );

        DataContext.Reset();

        var updated = await DataContext.Metadatas.FirstAsync(m => m.Id == metadata.Id);
        updated.TrainState.Should().Be(TrainState.Failed);
        updated.FailureReason.Should().Be("test failure");
    }

    [Test]
    public async Task ExecuteUpdateAsync_MultipleRows_UpdatesAll()
    {
        for (var i = 0; i < 4; i++)
        {
            var m = Metadata.Create(
                new CreateMetadata
                {
                    Name = typeof(ISchedulerTestTrain).FullName!,
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = new SchedulerTestInput { Value = $"batch-{i}" },
                }
            );
            await DataContext.Track(m);
        }

        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var count = await DataContext
            .Metadatas.Where(m => m.Name == typeof(ISchedulerTestTrain).FullName)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.TrainState, TrainState.Completed));

        count.Should().Be(4);

        DataContext.Reset();

        var all = await DataContext.Metadatas.ToListAsync();
        all.Should().OnlyContain(m => m.TrainState == TrainState.Completed);
    }

    #endregion

    #region BulkDelete_WithForeignKeys

    [Test]
    public async Task BulkDelete_LogsThenMetadata_RespectsForeignKeys()
    {
        // Run a train so both metadata and logs are created via the normal pipeline
        await TrainBus.RunAsync<Unit>(
            new SchedulerTestInput { Value = "fk-test" },
            CancellationToken.None
        );

        DataContext.Reset();

        var metadataCount = await DataContext.Metadatas.CountAsync();
        metadataCount.Should().BeGreaterThan(0);

        // Delete in FK-safe order: logs first, then metadata
        await DataContext.Logs.ExecuteDeleteAsync();
        var deleted = await DataContext.Metadatas.ExecuteDeleteAsync();

        deleted.Should().BeGreaterThan(0);

        var remaining = await DataContext.Metadatas.CountAsync();
        remaining.Should().Be(0);
    }

    #endregion
}
