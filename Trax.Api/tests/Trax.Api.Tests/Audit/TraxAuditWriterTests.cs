using AwesomeAssertions;
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
        TraxAuditOptions opts,
        Microsoft.Extensions.Logging.ILogger<TraxAuditWriter>? logger = null
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
            logger ?? NullLogger<TraxAuditWriter>.Instance
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
    public async Task Drains_EntriesArrivingWhileABatchFills_JoinThatBatch()
    {
        var sink = new RecordingSink();
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 3,
                // Longer than the test: only a full batch is written before the stop.
                FlushInterval = TimeSpan.FromMinutes(5),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            await writer.StartAsync(CancellationToken.None);
            channel.TryEnqueue(SampleEntry("a"));
            // The writer has taken "a" and is waiting for the batch to fill.
            await WaitUntilAsync(() => channel.Reader.Count == 0, TimeSpan.FromSeconds(10));
            channel.TryEnqueue(SampleEntry("b"));
            channel.TryEnqueue(SampleEntry("c"));
            await WaitUntilAsync(() => sink.Batches.Count >= 1, TimeSpan.FromSeconds(10));

            sink.Batches.Should().ContainSingle();
            sink.Batches[0].Select(e => e.PrincipalId).Should().Equal("a", "b", "c");

            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await writer.StopAsync(shutdown.Token);
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
    public async Task LoopFault_DropsAndCountsTheBatch_AndKeepsWriting()
    {
        // A fault outside the sink call (here the retry's warning log throws) cannot be retried;
        // the writer counts the batch as dropped and carries on with the next one.
        var sink = new FailingSink(failUntil: 1);
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 1,
                FlushInterval = TimeSpan.FromMilliseconds(20),
                MaxRetries = 3,
                RetryBackoff = TimeSpan.FromMilliseconds(5),
                ChannelCapacity = 100,
            },
            new WarningThrowingLogger()
        );
        using (sp)
        {
            await writer.StartAsync(CancellationToken.None);
            channel.TryEnqueue(SampleEntry("lost"));
            await WaitUntilAsync(() => channel.TotalDropped >= 1, TimeSpan.FromSeconds(10));
            channel.TryEnqueue(SampleEntry("kept"));
            await WaitUntilAsync(() => sink.Attempts >= 2, TimeSpan.FromSeconds(10));

            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await writer.StopAsync(shutdown.Token);

            channel.TotalDropped.Should().Be(1);
            sink.Attempts.Should().Be(2, "the second entry was written on its first attempt");
        }
    }

    /// <summary>A logger that throws when asked to write a warning, and only then.</summary>
    private sealed class WarningThrowingLogger
        : Microsoft.Extensions.Logging.ILogger<TraxAuditWriter>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning)
                throw new InvalidOperationException("log sink unavailable");
        }
    }

    [Test]
    public async Task Stop_SinkHonoursCancellation_StopsAtTheShutdownTimeout_AndCountsTheBatchOnce()
    {
        var sink = new CancellableHangingSink();
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 2,
                FlushInterval = TimeSpan.FromMilliseconds(20),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            await writer.StartAsync(CancellationToken.None);
            channel.TryEnqueue(SampleEntry("a"));
            channel.TryEnqueue(SampleEntry("b"));
            await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            using var shutdown = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await writer.StopAsync(shutdown.Token).WaitAsync(TimeSpan.FromSeconds(10));
            await sink.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));

            channel.TotalDropped.Should().Be(2);
        }
    }

    /// <summary>A sink whose write completes only when its token is cancelled.</summary>
    private sealed class CancellableHangingSink : ITraxAuditSink
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
        {
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                Cancelled.TrySetResult();
            }
        }
    }

    [Test]
    public async Task Stop_BatchFinishingAfterShutdownGaveUp_IsNotCountedTwice()
    {
        var sink = new ReleasableSink();
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 1,
                FlushInterval = TimeSpan.FromMilliseconds(20),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            await writer.StartAsync(CancellationToken.None);
            channel.TryEnqueue(SampleEntry("a"));
            await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            using var shutdown = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await writer.StopAsync(shutdown.Token).WaitAsync(TimeSpan.FromSeconds(10));
            channel.TotalDropped.Should().Be(1, "shutdown gave up on the batch");

            // The sink returns after all. The batch was already counted when shutdown gave up.
            sink.Release.SetResult();
            await writer.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

            channel.TotalDropped.Should().Be(1);
        }
    }

    /// <summary>A sink whose write ignores cancellation and completes when released.</summary>
    private sealed class ReleasableSink : ITraxAuditSink
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct)
        {
            Entered.TrySetResult();
            return Release.Task;
        }
    }

    [Test]
    public async Task Stop_CalledTwiceAtOnce_CountsEachUnwrittenEntryOnce()
    {
        var sink = new HangingSink();
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 2,
                FlushInterval = TimeSpan.FromMilliseconds(20),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            await writer.StartAsync(CancellationToken.None);
            for (var i = 0; i < 3; i++)
                channel.TryEnqueue(SampleEntry($"e{i}"));
            await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            using var shutdown = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Task.WhenAll(writer.StopAsync(shutdown.Token), writer.StopAsync(shutdown.Token))
                .WaitAsync(TimeSpan.FromSeconds(10));

            channel.TotalDropped.Should().Be(3);
        }
    }

    [Test]
    public async Task Stop_IdleWriterWithNoTimeLeft_DropsNothing()
    {
        var sink = new RecordingSink();
        var (channel, writer, sp) = Build(
            sink,
            new TraxAuditOptions
            {
                BatchSize = 2,
                FlushInterval = TimeSpan.FromMilliseconds(20),
                ChannelCapacity = 100,
            }
        );
        using (sp)
        {
            await writer.StartAsync(CancellationToken.None);

            await writer.StopAsync(new CancellationToken(canceled: true));

            channel.TotalDropped.Should().Be(0);
        }
    }

    [Test]
    public void RetryDelay_NeverExceedsTheCap_AtAnyAttempt()
    {
        var cap = TimeSpan.FromSeconds(30);
        var baseDelay = TimeSpan.FromMilliseconds(100);

        for (var attempt = 0; attempt <= 100; attempt++)
        {
            var delay = TraxAuditWriter.RetryDelay(attempt, baseDelay, cap);

            delay.Should().BeLessThanOrEqualTo(cap, $"attempt {attempt}");
            delay.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero, $"attempt {attempt}");
        }
    }

    [Test]
    public void RetryDelay_GrowsExponentially_WithJitterInTheUpperHalf()
    {
        var baseDelay = TimeSpan.FromMilliseconds(100);
        var cap = TimeSpan.FromSeconds(30);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var ceiling = TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt));
            var delays = Enumerable
                .Range(0, 50)
                .Select(_ => TraxAuditWriter.RetryDelay(attempt, baseDelay, cap))
                .ToList();

            delays.Should().OnlyContain(d => d >= ceiling / 2 && d <= ceiling);
            delays.Distinct().Should().HaveCountGreaterThan(1, "the delay is jittered");
        }
    }

    [Test]
    public void RetryDelay_AtTheCap_StaysWithinIt()
    {
        var cap = TimeSpan.FromSeconds(30);

        var delays = Enumerable
            .Range(0, 50)
            .Select(_ => TraxAuditWriter.RetryDelay(40, TimeSpan.FromMilliseconds(100), cap));

        delays.Should().OnlyContain(d => d >= cap / 2 && d <= cap);
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
