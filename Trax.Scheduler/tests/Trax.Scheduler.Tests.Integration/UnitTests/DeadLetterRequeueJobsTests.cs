using AwesomeAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Scheduler.Services.DeadLetterRequeue;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// The background requeue-all the dashboard and the GraphQL API both start: it answers at once
/// with a handle, runs the fold under the host's stopping token rather than its caller's, runs one
/// fold per node, and keeps finished jobs for a bounded time and number. One implementation here
/// means the two surfaces cannot drift apart.
///
/// <para>Enforces <c>Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0022-the-dashboard-and-the-api-share-one-operation-per-action.md")]
[TestFixture]
public class DeadLetterRequeueJobsTests
{
    private const int Awaiting = 3;

    private ITraxScheduler _scheduler = null!;
    private TaskCompletionSource<BatchDeadLetterResult> _fold = null!;
    private TaskCompletionSource _foldStarted = null!;
    private CancellationToken _foldToken;
    private CancellationTokenSource _stopping = null!;
    private ManualTime _time = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public async Task SetUp()
    {
        _fold = new TaskCompletionSource<BatchDeadLetterResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _foldStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _scheduler = Substitute.For<ITraxScheduler>();
        _scheduler
            .RequeueAllDeadLettersAsync(Arg.Any<CancellationToken>())
            .Returns(call => Fold(call.Arg<CancellationToken>()));
        _scheduler
            .RequeueAllDeadLettersAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => Fold(call.Arg<CancellationToken>()));
        _scheduler
            .RequeueAllDeadLettersAsync(
                Arg.Any<bool>(),
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => Fold(call.Arg<CancellationToken>()));

        _stopping = new CancellationTokenSource();
        _time = new ManualTime(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

        var factory = new InMemoryContextProviderFactory(new InMemoryDatabaseRoot());
        await using (var db = await factory.CreateDbContextAsync(default))
        {
            var manifest = Manifest.Create(new CreateManifest { Name = typeof(IJobTrain) });
            await db.Track(manifest);
            for (var i = 0; i < Awaiting + 2; i++)
            {
                var deadLetter = DeadLetter.Create(
                    new CreateDeadLetter
                    {
                        Manifest = manifest,
                        Reason = $"failure-{i}",
                        RetryCount = 3,
                    }
                );
                if (i >= Awaiting)
                    deadLetter.Acknowledge("handled");
                await db.Track(deadLetter);
            }
            await db.SaveChanges(default);
        }

        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(_stopping.Token);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataContextProviderFactory>(factory);
        services.AddSingleton(lifetime);
        services.AddSingleton<TimeProvider>(_time);
        services.AddScoped(_ => _scheduler);
        services.AddSingleton<IDeadLetterRequeueJobs, DeadLetterRequeueJobs>();
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public async Task TearDown()
    {
        _fold.TrySetCanceled();
        await _provider.DisposeAsync();
        _stopping.Dispose();
    }

    private Task<BatchDeadLetterResult> Fold(CancellationToken ct)
    {
        _foldToken = ct;
        _foldStarted.TrySetResult();
        return _fold.Task.WaitAsync(ct);
    }

    private DeadLetterRequeueJobs Jobs =>
        (DeadLetterRequeueJobs)_provider.GetRequiredService<IDeadLetterRequeueJobs>();

    [Test]
    public async Task Start_returns_a_running_job_counting_what_awaits()
    {
        var job = await Jobs.StartAsync();

        job.Status.Should().Be(DeadLetterRequeueJobStatus.Running);
        job.AwaitingAtStart.Should().Be(Awaiting, "acknowledged dead letters are not counted");
        job.Started.Should().BeTrue();
        job.AskAfresh.Should().BeFalse();
        job.Count.Should().BeNull();
        job.FinishedAt.Should().BeNull();
        job.StartedAt.Should().Be(_time.GetUtcNow().UtcDateTime);
        job.Message.Should().Be("Requeueing 3 dead letter(s) awaiting intervention.");
        Jobs.Get(job.Id).Should().Be(job);
    }

    [Test]
    public async Task The_fold_runs_under_the_hosts_stopping_token_not_the_callers()
    {
        using var caller = new CancellationTokenSource();
        await Jobs.StartAsync(ct: caller.Token);

        await caller.CancelAsync();
        await _foldStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        _foldToken.Should().Be(_stopping.Token);
        Jobs.Current.IsCompleted.Should().BeFalse("the fold runs on after its caller is gone");
    }

    [Test]
    public async Task A_finished_fold_is_read_as_succeeded_with_its_result()
    {
        var id = (await Jobs.StartAsync()).Id;

        _fold.SetResult(new BatchDeadLetterResult(Awaiting, "3 dead letter(s) requeued."));
        await Jobs.Current;

        var job = Jobs.Get(id)!;
        job.Status.Should().Be(DeadLetterRequeueJobStatus.Succeeded);
        job.Count.Should().Be(Awaiting);
        job.Message.Should().Be("3 dead letter(s) requeued.");
        job.FinishedAt.Should().NotBeNull();
    }

    [Test]
    public async Task A_fold_stopped_by_the_host_is_read_as_canceled()
    {
        var id = (await Jobs.StartAsync()).Id;

        await _stopping.CancelAsync();
        await Jobs.Current;

        var job = Jobs.Get(id)!;
        job.Status.Should().Be(DeadLetterRequeueJobStatus.Canceled);
        job.Message.Should().Be(DeadLetterRequeueJobs.CanceledMessage);
        job.Count.Should().BeNull();
    }

    [Test]
    public async Task A_failed_fold_says_nothing_about_the_server()
    {
        var id = (await Jobs.StartAsync()).Id;

        _fold.SetException(new InvalidOperationException("connection to 10.0.0.5 refused"));
        await Jobs.Current;

        var job = Jobs.Get(id)!;
        job.Status.Should().Be(DeadLetterRequeueJobStatus.Failed);
        job.Message.Should().Be(DeadLetterRequeueJobs.FailedMessage);
        job.Message.Should().NotContain("10.0.0.5");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task The_fold_is_asked_in_the_jobs_mode_with_a_progress_reporter(bool askAfresh)
    {
        var job = await Jobs.StartAsync(askAfresh);
        await _foldStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        job.AskAfresh.Should().Be(askAfresh);
        await _scheduler
            .Received(1)
            .RequeueAllDeadLettersAsync(
                askAfresh,
                Arg.Is<IProgress<int>?>(p => p != null),
                _stopping.Token
            );
    }

    [Test]
    public async Task A_running_job_says_how_many_it_has_requeued_so_far()
    {
        // Captured here rather than in the shared field, which a fold another test left running
        // could still write. For the same reason the test waits on its own signal, set once the
        // reporter is captured, rather than on _foldStarted: a fold left over from another test
        // can complete that one first.
        IProgress<int>? progress = null;
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _scheduler
            .RequeueAllDeadLettersAsync(
                Arg.Any<bool>(),
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                progress = call.Arg<IProgress<int>?>();
                captured.TrySetResult();
                return Fold(call.Arg<CancellationToken>());
            });
        var id = (await Jobs.StartAsync()).Id;
        await captured.Task.WaitAsync(TimeSpan.FromSeconds(10));

        progress!.Report(2);

        var running = Jobs.Get(id)!;
        running.Status.Should().Be(DeadLetterRequeueJobStatus.Running);
        running.Processed.Should().Be(2);
        running.Message.Should().Be("Requeued 2 of 3 dead letter(s) awaiting intervention so far.");

        _fold.SetResult(new BatchDeadLetterResult(Awaiting, "done"));
        await Jobs.Current;
        Jobs.Get(id)!.Processed.Should().Be(Awaiting);
    }

    [Test]
    public async Task Starting_while_one_runs_returns_that_one_and_starts_nothing()
    {
        var first = await Jobs.StartAsync();
        var second = await Jobs.StartAsync();

        second.Id.Should().Be(first.Id);
        second.Started.Should().BeFalse();
        second.Message.Should().Be(first.Message);
        await _scheduler
            .Received(1)
            .RequeueAllDeadLettersAsync(
                Arg.Any<bool>(),
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>()
            );
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Starting_in_the_other_mode_while_one_runs_says_it_was_not_started(
        bool runningAsksAfresh
    )
    {
        var running = await Jobs.StartAsync(runningAsksAfresh);
        var refused = await Jobs.StartAsync(!runningAsksAfresh);

        refused.Id.Should().Be(running.Id);
        refused.Started.Should().BeFalse();
        refused.AskAfresh.Should().Be(runningAsksAfresh, "it is the running job");
        refused.Message.Should().Be(DeadLetterRequeueJobs.OtherModeMessage(runningAsksAfresh));
        Jobs.Get(running.Id)!
            .Message.Should()
            .Be(running.Message, "the running job itself is not changed");
    }

    [Test]
    public async Task Starting_after_the_last_one_finished_starts_a_new_one()
    {
        var first = await Jobs.StartAsync();
        _fold.SetResult(new BatchDeadLetterResult(Awaiting, "done"));
        await Jobs.Current;

        var second = await Jobs.StartAsync();

        second.Id.Should().NotBe(first.Id);
        second.Started.Should().BeTrue();
    }

    [Test]
    public void An_unknown_id_is_null() => Jobs.Get(Guid.NewGuid()).Should().BeNull();

    [Test]
    public async Task A_finished_job_is_forgotten_after_the_retention()
    {
        var id = (await Jobs.StartAsync()).Id;
        _fold.SetResult(new BatchDeadLetterResult(Awaiting, "done"));
        await Jobs.Current;

        _time.Advance(DeadLetterRequeueJobs.Retention - TimeSpan.FromMinutes(1));
        Jobs.Get(id).Should().NotBeNull("a finished job is kept for 24 hours");

        _time.Advance(TimeSpan.FromMinutes(2));
        Jobs.Get(id).Should().BeNull("a finished job is forgotten after 24 hours");
    }

    [Test]
    public async Task A_running_job_is_never_forgotten()
    {
        var id = (await Jobs.StartAsync()).Id;

        _time.Advance(DeadLetterRequeueJobs.Retention * 2);

        Jobs.Get(id)!.Status.Should().Be(DeadLetterRequeueJobStatus.Running);
    }

    [Test]
    public async Task Finished_jobs_are_kept_up_to_the_limit_oldest_forgotten_first()
    {
        _scheduler
            .RequeueAllDeadLettersAsync(
                Arg.Any<bool>(),
                Arg.Any<IProgress<int>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new BatchDeadLetterResult(0, "nothing to requeue"));
        var jobs = Jobs;

        var ids = new List<Guid>();
        for (var i = 0; i <= DeadLetterRequeueJobs.MaxRetained; i++)
        {
            ids.Add(jobs.Start(0).Id);
            await jobs.Current;
            _time.Advance(TimeSpan.FromSeconds(1));
        }
        jobs.Start(0);
        await jobs.Current;

        jobs.Get(ids[0]).Should().BeNull("at most 100 finished jobs are kept");
        jobs.Get(ids[^1]).Should().NotBeNull();
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private interface IJobTrain;
}
