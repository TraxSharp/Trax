using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trax.Api.GraphQL.Audit;

namespace Trax.Api.Tests.Audit;

[TestFixture]
public class TraxAuditWriterTests
{
    private static TraxAuditEntry SampleEntry(string id) =>
        new(id, "apikey", "op", "{ x }", null, 1, DateTimeOffset.UtcNow, true, null);

    private sealed class RecordingSink : ITraxAuditSink
    {
        public List<List<TraxAuditEntry>> Batches { get; } = [];

        public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
        {
            Batches.Add([.. batch]);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingSink(int failUntil) : ITraxAuditSink
    {
        public int Attempts { get; private set; }

        public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
        {
            Attempts++;
            if (Attempts <= failUntil)
                throw new InvalidOperationException("sink down");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Polls <paramref name="predicate"/> until it returns true or
    /// <paramref name="timeout"/> elapses. Use instead of fixed
    /// <c>Task.Delay</c>s when waiting for the audit writer to flush a batch
    /// or accumulate retry attempts: CI scheduling can stretch flush-loop
    /// timing well past historical local timings, which races a fixed sleep.
    /// Polling on the actual completion condition finishes as soon as it
    /// appears with the timeout serving only as a safety ceiling.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;
            await Task.Delay(20);
        }
    }

    private static (TraxAuditChannel channel, TraxAuditWriter writer, ServiceProvider sp) Build(
        ITraxAuditSink sink,
        TraxAuditOptions opts
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITraxAuditSink>(sink);
        services.AddSingleton(Options.Create(opts));
        var channel = new TraxAuditChannel(
            Options.Create(opts),
            NullLogger<TraxAuditChannel>.Instance
        );
        services.AddSingleton(channel);
        var sp = services.BuildServiceProvider();

        var writer = new TraxAuditWriter(
            channel,
            sp,
            Options.Create(opts),
            TimeProvider.System,
            NullLogger<TraxAuditWriter>.Instance
        );
        return (channel, writer, sp);
    }

    [Test]
    public async Task Drains_BatchFull_FlushesImmediately()
    {
        var sink = new RecordingSink();
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 2,
                FlushInterval = TimeSpan.FromSeconds(30),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            channel.TryEnqueue(SampleEntry("a"));
            channel.TryEnqueue(SampleEntry("b"));
            channel.TryEnqueue(SampleEntry("c"));

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var writerTask = writer.StartAsync(cts.Token);
            await writerTask;
            await WaitUntilAsync(() => sink.Batches.Count >= 1, TimeSpan.FromSeconds(10));

            sink.Batches.Should().ContainSingle();
            sink.Batches[0].Select(e => e.PrincipalId).Should().BeEquivalentTo(["a", "b"]);

            await writer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Drains_PartialBatch_FlushesOnInterval()
    {
        var sink = new RecordingSink();
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 50,
                FlushInterval = TimeSpan.FromMilliseconds(100),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await writer.StartAsync(cts.Token);

            channel.TryEnqueue(SampleEntry("a"));
            channel.TryEnqueue(SampleEntry("b"));

            await WaitUntilAsync(
                () => sink.Batches.SelectMany(b => b).Count() >= 2,
                TimeSpan.FromSeconds(10)
            );
            sink.Batches.Should().NotBeEmpty();
            sink.Batches.SelectMany(b => b).Select(e => e.PrincipalId).Should().Contain(["a", "b"]);

            await writer.StopAsync(CancellationToken.None);
        }
    }

    private sealed class AlwaysFailingSink : ITraxAuditSink
    {
        public int Attempts { get; private set; }

        public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
        {
            Attempts++;
            throw new InvalidOperationException("sink permanently down");
        }
    }

    [Test]
    public async Task SinkThrowsBeyondMaxRetries_DropsBatch()
    {
        var sink = new AlwaysFailingSink();
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 1,
                FlushInterval = TimeSpan.FromMilliseconds(100),
                MaxRetries = 2,
                RetryBackoff = TimeSpan.FromMilliseconds(5),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await writer.StartAsync(cts.Token);
            channel.TryEnqueue(SampleEntry("a"));

            // Wait for the writer to exhaust retries and drop the batch.
            // (MaxRetries=2 means 3 total attempts before dropping.)
            await WaitUntilAsync(() => sink.Attempts >= 3, TimeSpan.FromSeconds(10));

            sink.Attempts.Should().BeGreaterThanOrEqualTo(3);

            await writer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task SinkThrowsBeyondMaxRetries_CountsTheDroppedEntries()
    {
        // The docs tell operators to alert on trax.audit.dropped: "A dropped entry is an
        // invisible operation." A batch the sink refuses for good is dropped here too.
        var sink = new AlwaysFailingSink();
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 2,
                FlushInterval = TimeSpan.FromMilliseconds(100),
                MaxRetries = 2,
                RetryBackoff = TimeSpan.FromMilliseconds(5),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            channel.TryEnqueue(SampleEntry("a"));
            channel.TryEnqueue(SampleEntry("b"));
            await writer.StartAsync(cts.Token);

            await WaitUntilAsync(() => sink.Attempts >= 3, TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => channel.TotalDropped >= 2, TimeSpan.FromSeconds(2));

            channel
                .TotalDropped.Should()
                .Be(2, "both entries were accepted and then dropped when the sink kept failing");

            await writer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Stop_DuringRetryBackoff_StopsAtTheShutdownTimeout_AndCountsTheBatchDropped()
    {
        var sink = new AlwaysFailingSink();
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 1,
                FlushInterval = TimeSpan.FromMilliseconds(100),
                MaxRetries = 10,
                // Long backoff so the writer is sleeping when the shutdown timeout fires.
                RetryBackoff = TimeSpan.FromSeconds(5),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            await writer.StartAsync(CancellationToken.None);
            channel.TryEnqueue(SampleEntry("a"));
            await WaitUntilAsync(() => sink.Attempts >= 1, TimeSpan.FromSeconds(10));

            // The host hands StopAsync a token that fires at HostOptions.ShutdownTimeout.
            using var shutdown = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var stopping = writer.StopAsync(shutdown.Token);
            // Throws TimeoutException if stopping is not bounded by the shutdown timeout.
            await stopping.WaitAsync(TimeSpan.FromSeconds(10));

            channel.TotalDropped.Should().Be(1, "the batch was never written");
        }
    }

    [Test]
    public async Task GracefulStop_WritesEveryAcceptedEntry()
    {
        var sink = new RecordingSink();
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 4,
                // Longer than the test: only the shutdown drain can write the partial batch.
                FlushInterval = TimeSpan.FromMinutes(5),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            await writer.StartAsync(CancellationToken.None);
            for (var i = 0; i < 10; i++)
                channel.TryEnqueue(SampleEntry($"e{i}")).Should().BeTrue();

            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await writer.StopAsync(shutdown.Token);

            sink.Batches.SelectMany(b => b)
                .Select(e => e.PrincipalId)
                .Should()
                .BeEquivalentTo(Enumerable.Range(0, 10).Select(i => $"e{i}"));
            channel.TotalDropped.Should().Be(0);
        }
    }

    [Test]
    public async Task Stop_SinkNeverReturns_StopsAtTheShutdownTimeout_AndCountsEveryUnwrittenEntry()
    {
        var sink = new HangingSink();
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 2,
                FlushInterval = TimeSpan.FromMilliseconds(50),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            await writer.StartAsync(CancellationToken.None);
            for (var i = 0; i < 5; i++)
                channel.TryEnqueue(SampleEntry($"e{i}"));
            await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            using var shutdown = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var stopping = writer.StopAsync(shutdown.Token);
            // A sink that ignores cancellation cannot hold shutdown open past its timeout.
            await stopping.WaitAsync(TimeSpan.FromSeconds(10));

            channel
                .TotalDropped.Should()
                .Be(5, "two were in the stuck batch, three never left the channel");
            channel.TryEnqueue(SampleEntry("late")).Should().BeFalse();
            channel
                .TotalDropped.Should()
                .Be(6, "an entry offered after shutdown is refused and counted");
        }
    }

    /// <summary>A sink whose write never completes and ignores its cancellation token.</summary>
    private sealed class HangingSink : ITraxAuditSink
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
        {
            Entered.TrySetResult();
            return new TaskCompletionSource().Task;
        }
    }

    [Test]
    public async Task Drains_QuietChannel_Stops_WithoutFlushing()
    {
        var sink = new RecordingSink();
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 50,
                FlushInterval = TimeSpan.FromSeconds(60),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            await writer.StartAsync(CancellationToken.None);
            // Don't enqueue anything. Stop the writer; the empty-batch waiting
            // path inside DrainBatchAsync should yield without error.
            await Task.Delay(150);
            await writer.StopAsync(CancellationToken.None);

            sink.Batches.Should().BeEmpty();
        }
    }

    [Test]
    public async Task SinkThrows_Retries_ThenSucceeds()
    {
        var sink = new FailingSink(failUntil: 2);
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 1,
                FlushInterval = TimeSpan.FromMilliseconds(100),
                MaxRetries = 3,
                RetryBackoff = TimeSpan.FromMilliseconds(10),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await writer.StartAsync(cts.Token);
            channel.TryEnqueue(SampleEntry("a"));

            await WaitUntilAsync(() => sink.Attempts >= 3, TimeSpan.FromSeconds(10));

            sink.Attempts.Should().BeGreaterThanOrEqualTo(3);

            await writer.StopAsync(CancellationToken.None);
        }
    }
}
