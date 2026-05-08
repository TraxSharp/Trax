using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="IOperationsService"/>. These exercise the shared
/// validation + persistence path used by both the dashboard UI and the GraphQL
/// <c>operations.workQueue</c> mutations, so coverage here also covers the dashboard
/// QueueTrainDialog and the GraphQL WorkQueueMutations behaviour.
/// </summary>
[TestFixture]
public class OperationsServiceTests : TestSetup
{
    private IOperationsService _operations = null!;

    [SetUp]
    public void GetService()
    {
        _operations = Scope.ServiceProvider.GetRequiredService<IOperationsService>();
    }

    private static string TrainName => typeof(ISchedulerTestTrain).FullName!;

    #region QueueTrainAsync

    [Test]
    public async Task QueueTrainAsync_HappyPath_PersistsEntry()
    {
        var result = await _operations.QueueTrainAsync(
            new QueueTrainInput(TrainName, """{"value":"hello"}""", Priority: 5),
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        result.Id.Should().NotBeNull();
        result.Count.Should().Be(1);

        var entries = DataContext.WorkQueues.ToList();
        entries.Should().HaveCount(1);
        entries[0].TrainName.Should().Be(typeof(ISchedulerTestTrain).FullName);
        entries[0].InputTypeName.Should().Be(typeof(SchedulerTestInput).FullName);
        entries[0].Priority.Should().Be(5);
        entries[0].Status.Should().Be(WorkQueueStatus.Queued);
        entries[0].Input.Should().Contain("hello");
    }

    [Test]
    public async Task QueueTrainAsync_NullInputJson_PersistsWithNullInput()
    {
        var result = await _operations.QueueTrainAsync(
            new QueueTrainInput(TrainName),
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        DataContext.WorkQueues.Single().Input.Should().BeNull();
    }

    [Test]
    public async Task QueueTrainAsync_WhitespaceInputJson_PersistsWithNullInput()
    {
        var result = await _operations.QueueTrainAsync(
            new QueueTrainInput(TrainName, InputJson: "   "),
            CancellationToken.None
        );

        result.Success.Should().BeTrue();
        DataContext.WorkQueues.Single().Input.Should().BeNull();
    }

    [Test]
    public async Task QueueTrainAsync_ScheduledAt_IsPersisted()
    {
        var when = DateTime.UtcNow.AddMinutes(10);

        await _operations.QueueTrainAsync(
            new QueueTrainInput(TrainName, ScheduledAt: when),
            CancellationToken.None
        );

        DataContext
            .WorkQueues.Single()
            .ScheduledAt.Should()
            .BeCloseTo(when, TimeSpan.FromSeconds(1));
    }

    [Test]
    public async Task QueueTrainAsync_EmptyTrainName_ReturnsFailure()
    {
        var result = await _operations.QueueTrainAsync(
            new QueueTrainInput(""),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("TrainName");
        DataContext.WorkQueues.Should().BeEmpty();
    }

    [Test]
    public async Task QueueTrainAsync_WhitespaceTrainName_ReturnsFailure()
    {
        var result = await _operations.QueueTrainAsync(
            new QueueTrainInput("   "),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        DataContext.WorkQueues.Should().BeEmpty();
    }

    [Test]
    public async Task QueueTrainAsync_UnknownTrainName_ReturnsFailureNoInsert()
    {
        var result = await _operations.QueueTrainAsync(
            new QueueTrainInput("Trax.Unknown.IDoesNotExist"),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Unknown train");
        DataContext.WorkQueues.Should().BeEmpty();
    }

    [Test]
    public async Task QueueTrainAsync_InvalidJson_ReturnsFailureNoInsert()
    {
        var result = await _operations.QueueTrainAsync(
            new QueueTrainInput(TrainName, InputJson: "{not valid json"),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Invalid InputJson");
        DataContext.WorkQueues.Should().BeEmpty();
    }

    [Test]
    public async Task QueueTrainAsync_JsonNullLiteral_ReturnsFailure()
    {
        var result = await _operations.QueueTrainAsync(
            new QueueTrainInput(TrainName, InputJson: "null"),
            CancellationToken.None
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("null");
        DataContext.WorkQueues.Should().BeEmpty();
    }

    [Test]
    public async Task QueueTrainAsync_PriorityOver31_IsClamped()
    {
        await _operations.QueueTrainAsync(
            new QueueTrainInput(TrainName, Priority: 9999),
            CancellationToken.None
        );

        DataContext.WorkQueues.Single().Priority.Should().Be(WorkQueue.MaxPriority);
    }

    [Test]
    public async Task QueueTrainAsync_PriorityNegative_IsClamped()
    {
        await _operations.QueueTrainAsync(
            new QueueTrainInput(TrainName, Priority: -50),
            CancellationToken.None
        );

        DataContext.WorkQueues.Single().Priority.Should().Be(WorkQueue.MinPriority);
    }

    #endregion

    #region CancelWorkQueueEntryAsync

    [Test]
    public async Task CancelWorkQueueEntryAsync_QueuedEntry_TransitionsToCancelled()
    {
        var queued = WorkQueue.Create(new CreateWorkQueue { TrainName = TrainName });
        await DataContext.Track(queued);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var result = await _operations.CancelWorkQueueEntryAsync(queued.Id, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Id.Should().Be(queued.Id);
        result.Count.Should().Be(1);

        DataContext.Reset();
        DataContext.WorkQueues.Single().Status.Should().Be(WorkQueueStatus.Cancelled);
    }

    [Test]
    public async Task CancelWorkQueueEntryAsync_MissingId_ReturnsFailure()
    {
        var result = await _operations.CancelWorkQueueEntryAsync(99999, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Test]
    public async Task CancelWorkQueueEntryAsync_AlreadyCancelled_ReturnsFailure()
    {
        var entry = WorkQueue.Create(new CreateWorkQueue { TrainName = TrainName });
        entry.Status = WorkQueueStatus.Cancelled;
        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var result = await _operations.CancelWorkQueueEntryAsync(entry.Id, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Cancelled");
    }

    [Test]
    public async Task CancelWorkQueueEntryAsync_AlreadyDispatched_ReturnsFailure()
    {
        var entry = WorkQueue.Create(new CreateWorkQueue { TrainName = TrainName });
        entry.Status = WorkQueueStatus.Dispatched;
        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var result = await _operations.CancelWorkQueueEntryAsync(entry.Id, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Dispatched");

        DataContext.Reset();
        DataContext.WorkQueues.Single().Status.Should().Be(WorkQueueStatus.Dispatched);
    }

    #endregion
}
