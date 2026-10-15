using System.Runtime.CompilerServices;
using AwesomeAssertions;
using HotChocolate.Execution;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.GraphQL.TypeModules;

namespace Trax.Api.Tests;

/// <summary>
/// A runProgress stream on a failed run of a manifest waits for its retry: it looks less often the longer it waits, and
/// stops looking once it is disposed, whether or not its reader was cancelled.
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

    private static RunProgressStream Failed(
        Func<RunProgressRun, CancellationToken, Task<(string? RetriedAs, bool NoRetry)>> nextTry,
        Func<TimeSpan, CancellationToken, Task> delay
    ) =>
        new(
            new RunProgressRun(
                new string('a', 32),
                "Train",
                null,
                1,
                RunProgressState.Failed,
                null,
                "starter",
                new LifecycleVisibility(false, new HashSet<string>(StringComparer.Ordinal))
            )
            {
                ManifestId = 1,
            },
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
