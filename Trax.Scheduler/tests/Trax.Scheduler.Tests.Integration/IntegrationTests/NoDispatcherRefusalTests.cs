using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.ChangeSignal;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.DeadLetterRequeue;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fakes;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// On a host with no database provider nothing dispatches the work queue, so every operation
/// that would queue a run is refused with <see cref="OperationsService.NoDispatcherMessage"/>,
/// writes nothing and signals nothing. Running a train now is still allowed.
///
/// <para>Enforces <c>docs/adr/0019-a-queued-run-is-refused-where-nothing-dispatches-it.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0019-a-queued-run-is-refused-where-nothing-dispatches-it.md")]
[TestFixture]
public class NoDispatcherRefusalTests
{
    private const string Because =
        "nothing dispatches a queued run without a database provider "
        + "(docs/adr/0019-a-queued-run-is-refused-where-nothing-dispatches-it.md)";

    private RecordingChangeSignal _signal = null!;
    private SchedulerE2EFixture _fx = null!;
    private IOperationsService _operations = null!;

    [SetUp]
    public void SetUp()
    {
        _signal = new RecordingChangeSignal();
        _fx = SchedulerE2EFixture.CreateInMemory(
            _ => { },
            services => services.AddSingleton<ITraxChangeSignal>(_signal)
        );
        _operations = _fx.Services.GetRequiredService<IOperationsService>();
    }

    [TearDown]
    public async Task TearDown() => await _fx.DisposeAsync();

    [Test]
    public async Task Queueing_a_train_is_refused()
    {
        var result = await _operations.QueueTrainAsync(
            new QueueTrainInput(typeof(ISchedulerTestTrain).FullName!),
            CancellationToken.None
        );

        result.Success.Should().BeFalse(Because);
        result.Message.Should().Be(OperationsService.NoDispatcherMessage);
        await NothingWasQueued();
    }

    [Test]
    public async Task Requeueing_an_execution_is_refused()
    {
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(ISchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        run.Input = """{"Value":"requeue"}""";
        await _fx.DataContext.Track(run);
        await _fx.DataContext.SaveChanges(CancellationToken.None);
        _fx.DataContext.Reset();

        var result = await _operations.RequeueExecutionAsync(run.Id, CancellationToken.None);

        result.Success.Should().BeFalse(Because);
        result.Message.Should().Be(OperationsService.NoDispatcherMessage);
        await NothingWasQueued();
    }

    [Test]
    public async Task Every_operator_trigger_is_refused()
    {
        var group = new ManifestGroup
        {
            Name = $"refused-{Guid.NewGuid():N}",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await _fx.DataContext.Track(group);
        await _fx.DataContext.SaveChanges(CancellationToken.None);
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                Properties = new SchedulerTestInput { Value = "refused" },
            }
        );
        manifest.ManifestGroupId = group.Id;
        await _fx.DataContext.Track(manifest);
        await _fx.DataContext.SaveChanges(CancellationToken.None);
        _fx.DataContext.Reset();
        _signal.Clear();

        var single = await _operations.TriggerManifestAsync(
            manifest.ExternalId,
            delay: null,
            askAfresh: false,
            CancellationToken.None
        );
        var batch = await _operations.TriggerManifestsAsync(
            [manifest.Id],
            askAfresh: false,
            CancellationToken.None
        );
        var groups = await _operations.TriggerManifestGroupsAsync(
            [group.Id],
            CancellationToken.None
        );

        single.Success.Should().BeFalse(Because);
        single.Message.Should().Be(OperationsService.NoDispatcherMessage);
        single.Trigger.Should().BeNull();
        batch.Success.Should().BeFalse(Because);
        batch.Message.Should().Be(OperationsService.NoDispatcherMessage);
        groups.Success.Should().BeFalse(Because);
        groups.Message.Should().Be(OperationsService.NoDispatcherMessage);
        await NothingWasQueued();
    }

    [Test]
    public async Task Running_a_train_now_is_still_allowed()
    {
        var result = await _operations.RunTrainAsync(
            new RunTrainInput(typeof(ISchedulerTestTrain).FullName!, """{"value":"now"}"""),
            CancellationToken.None
        );

        result.Message.Should().NotBe(OperationsService.NoDispatcherMessage);
    }

    [Test]
    public async Task Every_dead_letter_requeue_is_refused()
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                Properties = new SchedulerTestInput { Value = "dead" },
            }
        );
        await _fx.DataContext.Track(manifest);
        await _fx.DataContext.SaveChanges(CancellationToken.None);
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "seeded",
                RetryCount = 3,
            }
        );
        await _fx.DataContext.Track(deadLetter);
        await _fx.DataContext.SaveChanges(CancellationToken.None);
        _fx.DataContext.Reset();
        _signal.Clear();

        var scheduler = _fx.Services.GetRequiredService<ITraxScheduler>();
        var single = await scheduler.RequeueDeadLetterAsync(deadLetter.Id);
        var batch = await scheduler.RequeueDeadLettersAsync([deadLetter.Id]);
        var all = await scheduler.RequeueAllDeadLettersAsync();

        var jobs = (DeadLetterRequeueJobs)_fx.Services.GetRequiredService<IDeadLetterRequeueJobs>();
        var job = await jobs.StartAsync();
        await jobs.Current;

        single.Success.Should().BeFalse(Because);
        single.Message.Should().Be(OperationsService.NoDispatcherMessage);
        batch.Count.Should().Be(0);
        batch.Message.Should().Be(OperationsService.NoDispatcherMessage);
        all.Count.Should().Be(0);
        all.Message.Should().Be(OperationsService.NoDispatcherMessage);
        jobs.Get(job.Id)!.Message.Should().Be(OperationsService.NoDispatcherMessage);
        await NothingWasQueued();
        (await _fx.DataContext.DeadLetters.AsNoTracking().SingleAsync())
            .Status.Should()
            .Be(DeadLetterStatus.AwaitingIntervention, Because);
    }

    [Test]
    public async Task A_caller_the_train_refuses_is_told_that_rather_than_what_store_the_host_has()
    {
        await _fx.DisposeAsync();
        _fx = SchedulerE2EFixture.CreateInMemory(
            _ => { },
            services =>
                services
                    .AddSingleton<ITraxChangeSignal>(_signal)
                    .AddSingleton<ITrainAuthorizationService, RefusingAuthorization>()
        );
        _operations = _fx.Services.GetRequiredService<IOperationsService>();

        var queue = () =>
            _operations.QueueTrainAsync(
                new QueueTrainInput(typeof(ISchedulerTestTrain).FullName!),
                CancellationToken.None
            );

        await queue.Should().ThrowAsync<UnauthorizedAccessException>();
        await NothingWasQueued();
    }

    private sealed class RefusingAuthorization : ITrainAuthorizationService
    {
        public Task AuthorizeAsync(
            TrainRegistration registration,
            CancellationToken ct = default
        ) => throw new UnauthorizedAccessException("not this caller");
    }

    private async Task NothingWasQueued()
    {
        (await _fx.DataContext.WorkQueues.AsNoTracking().CountAsync()).Should().Be(0, Because);
        _signal.Domains.Should().NotContain(ChangeDomain.WorkQueue);
    }
}
