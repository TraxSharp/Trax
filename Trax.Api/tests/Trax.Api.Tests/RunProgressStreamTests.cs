using System.Runtime.CompilerServices;
using AwesomeAssertions;
using HotChocolate.Execution;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.StateMachine;

namespace Trax.Api.Tests;

/// <summary>
/// A runProgress stream on a failed run of a manifest waits for its retry: it looks less often the longer it waits, and
/// stops looking once it is disposed, whether or not its reader was cancelled. A stream on a queued run keeps reading
/// its fate after a failed read, less often each time, and ends with a problem when the reads keep failing.
/// </summary>
[TestFixture]
[Property("adr", Adr)]
public class RunProgressStreamTests
{
    private const string Adr =
        "Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md";

    [Test]
    public async Task Retry_polls_back_off()
    {
        var delays = new List<TimeSpan>();
        var looks = 0;
        await using var stream = Failed(
            (_, _) =>
                Task.FromResult<(string?, bool)>(++looks >= 8 ? ("retry", false) : (null, false)),
            (wait, _) =>
            {
                delays.Add(wait);
                return Task.CompletedTask;
            }
        );

        var events = new List<RunProgressEvent>();
        await foreach (var e in stream.ReadEventsAsync())
            events.Add(e);

        delays
            .Should()
            .Equal(
                new[] { 1, 2, 4, 8, 16, 30, 30 }.Select(s => TimeSpan.FromSeconds(s)),
                "a stream waiting for a retry looks once a second at first, then less often"
            );
        events.Last().RetriedAs.Should().Be("retry");
    }

    [Test]
    public async Task Disposing_a_stream_in_retry_wait_stops_its_polling()
    {
        var looks = 0;
        var stream = Failed(
            (_, _) =>
            {
                Interlocked.Increment(ref looks);
                return Task.FromResult<(string?, bool)>((null, false));
            },
            // allowed-delay: the stream's own wait between looks, kept short so the loop runs while the test watches.
            (_, ct) => Task.Delay(TimeSpan.FromMilliseconds(10), ct)
        );
        var reader = stream.ReadEventsAsync().GetAsyncEnumerator();
        (await reader.MoveNextAsync()).Should().BeTrue();
        var waiting = reader.MoveNextAsync().AsTask();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (Volatile.Read(ref looks) < 2 && DateTime.UtcNow < deadline)
            // allowed-delay: waits for the retry loop to start, bounded by the deadline.
            await Task.Delay(10);

        await stream.DisposeAsync();

        (await waiting.WaitAsync(TimeSpan.FromSeconds(5)))
            .Should()
            .BeFalse("a disposed stream ends");
        var after = Volatile.Read(ref looks);
        // allowed-delay: gives a loop that outlived its stream time to look again.
        await Task.Delay(200);
        Volatile
            .Read(ref looks)
            .Should()
            .Be(after, "a disposed stream stops looking for its retry");
        await reader.DisposeAsync();
    }

    [Test]
    public async Task A_queued_poll_that_throws_keeps_watching_and_ends_on_cancel()
    {
        var reads = 0;
        var lease = new Counted();
        await using var stream = new RunProgressStream(
            Run(RunProgressState.Queued),
            new Silent<TrainLifecycleEvent>(),
            0,
            new Silent<JunctionEvent>(),
            0,
            _ => ValueTask.FromResult(0L),
            lease
        )
        {
            ReadQueued = (_, _) =>
                ++reads == 1
                    ? Task.FromException<QueuedFate>(
                        new InvalidOperationException("connection dropped")
                    )
                    : Task.FromResult(QueuedFate.Cancelled),
            Delay = (_, _) => Task.CompletedTask,
        };

        var events = new List<RunProgressEvent>();
        await foreach (
            var e in stream
                .ReadEventsAsync()
                .WithCancellation(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token)
        )
            events.Add(e);

        reads.Should().Be(2, "a failed read is read again at the next poll");
        events
            .Select(e => e.State)
            .Should()
            .Equal(RunProgressState.Queued, RunProgressState.Cancelled);
        lease.Disposed.Should().Be(1, "the ended stream gives its place back");
    }

    [Test]
    public async Task A_queued_poll_that_keeps_failing_ends_with_a_problem()
    {
        var reads = 0;
        var waits = new List<TimeSpan>();
        var lease = new Counted();
        await using var stream = Queued(
            lease,
            (_, _) =>
            {
                reads++;
                return Task.FromException<QueuedFate>(new InvalidOperationException("no access"));
            },
            (wait, _) =>
            {
                waits.Add(wait);
                return Task.CompletedTask;
            }
        );

        var events = new List<RunProgressEvent>();
        await foreach (
            var e in stream
                .ReadEventsAsync()
                .WithCancellation(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token)
        )
            events.Add(e);

        reads.Should().Be(RunProgressStream.QueuedFailureBound);
        waits
            .Should()
            .Equal(
                new[] { 5, 10, 20, 30, 30, 30, 30, 30, 30, 30 }.Select(s =>
                    TimeSpan.FromSeconds(s)
                ),
                "a read that keeps failing is tried less often"
            );
        events.Should().HaveCount(2);
        events[1].Problem!.Code.Should().Be(RefusalCode.InternalError);
        events[1].Sequence.Should().Be(2);
        lease.Disposed.Should().Be(1, "the ended stream gives its place back");
    }

    [Test]
    public async Task A_queued_poll_cancelled_by_its_own_timeout_keeps_watching()
    {
        var reads = 0;
        var lease = new Counted();
        await using var stream = Queued(
            lease,
            (_, _) =>
                ++reads == 1
                    ? Task.FromException<QueuedFate>(new OperationCanceledException("timed out"))
                    : Task.FromResult(QueuedFate.Cancelled),
            (_, _) => Task.CompletedTask
        );

        var events = new List<RunProgressEvent>();
        await foreach (
            var e in stream
                .ReadEventsAsync()
                .WithCancellation(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token)
        )
            events.Add(e);

        reads.Should().Be(2, "a read cancelled by something other than the stream is read again");
        events
            .Select(e => e.State)
            .Should()
            .Equal(RunProgressState.Queued, RunProgressState.Cancelled);
        lease.Disposed.Should().Be(1);
    }

    private static RunProgressStream Queued(
        IDisposable lease,
        Func<RunProgressRun, CancellationToken, Task<QueuedFate>> read,
        Func<TimeSpan, CancellationToken, Task> delay
    ) =>
        new(
            Run(RunProgressState.Queued),
            new Silent<TrainLifecycleEvent>(),
            0,
            new Silent<JunctionEvent>(),
            0,
            _ => ValueTask.FromResult(0L),
            lease
        )
        {
            ReadQueued = read,
            Delay = delay,
        };

    private static RunProgressRun Run(RunProgressState state) =>
        new(
            new string('a', 32),
            "Train",
            null,
            1,
            state,
            null,
            "starter",
            new LifecycleVisibility(false, new HashSet<string>(StringComparer.Ordinal))
        )
        {
            ManifestId = 1,
        };

    private sealed class Counted : IDisposable
    {
        public int Disposed;

        public void Dispose() => Disposed++;
    }

    private static RunProgressStream Failed(
        Func<RunProgressRun, CancellationToken, Task<(string? RetriedAs, bool NoRetry)>> nextTry,
        Func<TimeSpan, CancellationToken, Task> delay
    ) =>
        new(
            Run(RunProgressState.Failed),
            new Silent<TrainLifecycleEvent>(),
            0,
            new Silent<JunctionEvent>(),
            0,
            _ => ValueTask.FromResult(0L),
            new Nothing()
        )
        {
            NextTry = nextTry,
            Delay = delay,
        };

    private sealed class Nothing : IDisposable
    {
        public void Dispose() { }
    }

    // A topic nothing is published on.
    private sealed class Silent<T> : ISourceStream<T>
    {
        IAsyncEnumerable<T> ISourceStream<T>.ReadEventsAsync() => Read<T>();

        IAsyncEnumerable<object?> ISourceStream.ReadEventsAsync() => Read<object?>();

        private static async IAsyncEnumerable<TOut> Read<TOut>(
            [EnumeratorCancellation] CancellationToken ct = default
        )
        {
            // allowed-delay: waits for the stream to be cancelled, not for a duration.
            await Task.Delay(Timeout.Infinite, ct);
            yield break;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
