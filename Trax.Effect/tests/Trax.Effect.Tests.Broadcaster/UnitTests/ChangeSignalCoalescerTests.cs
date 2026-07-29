using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Services.ChangeSignal;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

[TestFixture]
public class ChangeSignalCoalescerTests
{
    // A small window keeps the tests fast. Correctness (one flush per burst, distinct domains)
    // holds for any positive window, so the exact value does not race: the assertions poll on the
    // real flush, never on a fixed sleep.
    private static ChangeSignalOptions FastOptions() =>
        new() { CoalesceWindow = TimeSpan.FromMilliseconds(30) };

    private sealed class CapturingSink : IChangeSignalSink
    {
        private readonly object _lock = new();
        public List<ChangeDomain[]> Flushes { get; } = new();

        public Func<Task>? OnFlush { get; set; }

        public async Task FlushAsync(
            IReadOnlyCollection<ChangeDomain> domains,
            CancellationToken ct
        )
        {
            if (OnFlush is not null)
                await OnFlush();
            lock (_lock)
                Flushes.Add(domains.ToArray());
        }

        public int Count
        {
            get
            {
                lock (_lock)
                    return Flushes.Count;
            }
        }

        /// <summary>Thread-safe union of every domain delivered across all flushes.</summary>
        public HashSet<ChangeDomain> AllDomains()
        {
            lock (_lock)
                return Flushes.SelectMany(f => f).ToHashSet();
        }
    }

    private static (
        TraxChangeSignal Signal,
        ChangeSignalCoalescer Coalescer,
        ServiceProvider Provider
    ) Build(ChangeSignalOptions options, params IChangeSignalSink[] sinks)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        foreach (var sink in sinks)
            services.AddSingleton(sink);
        var provider = services.BuildServiceProvider();

        var signal = new TraxChangeSignal(options);
        var coalescer = new ChangeSignalCoalescer(
            signal,
            provider,
            options,
            TimeProvider.System,
            provider.GetService<ILogger<ChangeSignalCoalescer>>()
        );
        return (signal, coalescer, provider);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            // allowed-delay: poll interval, not a sleep-for-sync. Bounded by the caller's timeout
            // and exits the instant the condition holds.
            await Task.Delay(15);
        }
        return condition();
    }

    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task SameDomainBurst_CoalescesToSingleFlush()
    {
        var sink = new CapturingSink();
        var (signal, coalescer, provider) = Build(FastOptions(), sink);
        await using var _ = provider;

        // Buffer the burst before the coalescer starts, so the first drain sees the whole burst.
        signal.Notify(ChangeDomain.WorkQueue);
        signal.Notify(ChangeDomain.WorkQueue);
        signal.Notify(ChangeDomain.WorkQueue);

        await coalescer.StartAsync(CancellationToken.None);
        try
        {
            var ok = await WaitUntilAsync(() => sink.Count == 1, FlushTimeout);
            ok.Should().BeTrue("a burst of one domain should produce exactly one flush");
            sink.Flushes[0].Should().ContainSingle().Which.Should().Be(ChangeDomain.WorkQueue);
        }
        finally
        {
            await coalescer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task DistinctDomainsInWindow_FlushContainsEachOnce()
    {
        var sink = new CapturingSink();
        var (signal, coalescer, provider) = Build(FastOptions(), sink);
        await using var _ = provider;

        signal.Notify(ChangeDomain.WorkQueue);
        signal.Notify(ChangeDomain.DeadLetter);
        signal.Notify(ChangeDomain.WorkQueue);
        signal.Notify(ChangeDomain.Manifest);

        await coalescer.StartAsync(CancellationToken.None);
        try
        {
            var ok = await WaitUntilAsync(() => sink.Count == 1, FlushTimeout);
            ok.Should().BeTrue("distinct domains in one window collapse into a single flush");
            sink.Flushes[0]
                .Should()
                .BeEquivalentTo(
                    new[] { ChangeDomain.WorkQueue, ChangeDomain.DeadLetter, ChangeDomain.Manifest }
                );
        }
        finally
        {
            await coalescer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task SeparateBursts_ProduceSeparateFlushes()
    {
        var sink = new CapturingSink();
        var (signal, coalescer, provider) = Build(FastOptions(), sink);
        await using var _ = provider;

        await coalescer.StartAsync(CancellationToken.None);
        try
        {
            signal.Notify(ChangeDomain.WorkQueue);
            (await WaitUntilAsync(() => sink.Count == 1, FlushTimeout)).Should().BeTrue();

            // A signal that arrives after the first flush opens a new window.
            signal.Notify(ChangeDomain.DeadLetter);
            (await WaitUntilAsync(() => sink.Count == 2, FlushTimeout)).Should().BeTrue();

            sink.Flushes[0].Should().ContainSingle().Which.Should().Be(ChangeDomain.WorkQueue);
            sink.Flushes[1].Should().ContainSingle().Which.Should().Be(ChangeDomain.DeadLetter);
        }
        finally
        {
            await coalescer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task NoSignals_NeverFlushes_ThenWakesOnFirstSignal()
    {
        var sink = new CapturingSink();
        var (signal, coalescer, provider) = Build(FastOptions(), sink);
        await using var _ = provider;

        await coalescer.StartAsync(CancellationToken.None);
        try
        {
            // negative-wait: the coalescer blocks on an empty channel, so without a signal it
            // cannot flush. The bounded wait proves the absence; the outcome is structural, not
            // timing-dependent, so a longer delay would only slow the test, never change the result.
            await Task.Delay(150);
            sink.Count.Should().Be(0, "an idle coalescer must not flush");

            signal.Notify(ChangeDomain.SchedulerConfig);
            var ok = await WaitUntilAsync(() => sink.Count == 1, FlushTimeout);
            ok.Should().BeTrue("the coalescer wakes as soon as a real signal arrives");
            sink.Flushes[0]
                .Should()
                .ContainSingle()
                .Which.Should()
                .Be(ChangeDomain.SchedulerConfig);
        }
        finally
        {
            await coalescer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Flush_ReachesEverySink()
    {
        var sinkA = new CapturingSink();
        var sinkB = new CapturingSink();
        var (signal, coalescer, provider) = Build(FastOptions(), sinkA, sinkB);
        await using var _ = provider;

        signal.Notify(ChangeDomain.ManifestGroup);

        await coalescer.StartAsync(CancellationToken.None);
        try
        {
            var ok = await WaitUntilAsync(() => sinkA.Count == 1 && sinkB.Count == 1, FlushTimeout);
            ok.Should().BeTrue("every registered sink receives the flush");
            sinkA.Flushes[0].Should().ContainSingle().Which.Should().Be(ChangeDomain.ManifestGroup);
            sinkB.Flushes[0].Should().ContainSingle().Which.Should().Be(ChangeDomain.ManifestGroup);
        }
        finally
        {
            await coalescer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task SinkThrows_LoopKeepsRunning()
    {
        var throwing = new CapturingSink
        {
            OnFlush = () => throw new InvalidOperationException("boom"),
        };
        var healthy = new CapturingSink();
        var (signal, coalescer, provider) = Build(FastOptions(), throwing, healthy);
        await using var _ = provider;

        await coalescer.StartAsync(CancellationToken.None);
        try
        {
            signal.Notify(ChangeDomain.WorkQueue);
            // The healthy sink still receives the flush even though the other sink threw...
            (await WaitUntilAsync(() => healthy.Count == 1, FlushTimeout))
                .Should()
                .BeTrue();

            // ...and a later burst still flushes, proving the loop survived the exception.
            signal.Notify(ChangeDomain.DeadLetter);
            (await WaitUntilAsync(() => healthy.Count == 2, FlushTimeout)).Should().BeTrue();
        }
        finally
        {
            await coalescer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task UnderHeavyConcurrentLoad_CoalescesToFarFewerFlushesAndDeliversEveryDomain()
    {
        // The whole point of the coalescer is to bound refetches under a write storm. Hammer it
        // with 100k signals from several threads and prove two things: every distinct domain is
        // delivered, and the flush count collapses by orders of magnitude (not one flush per signal).
        var options = new ChangeSignalOptions
        {
            // Large buffer so this correctness test never exercises the drop path (covered separately).
            ChannelCapacity = 500_000,
            CoalesceWindow = TimeSpan.FromMilliseconds(20),
        };
        var sink = new CapturingSink();
        var (signal, coalescer, provider) = Build(options, sink);
        await using var _ = provider;

        await coalescer.StartAsync(CancellationToken.None);
        try
        {
            const int threads = 5;
            const int perThread = 20_000;
            const int total = threads * perThread;
            var domains = Enum.GetValues<ChangeDomain>();

            Exception? writerFailure = null;
            await Task.WhenAll(
                Enumerable
                    .Range(0, threads)
                    .Select(t =>
                        Task.Run(() =>
                        {
                            try
                            {
                                for (var i = 0; i < perThread; i++)
                                    signal.Notify(domains[(t + i) % domains.Length]);
                            }
                            catch (Exception ex)
                            {
                                writerFailure = ex;
                            }
                        })
                    )
            );

            writerFailure.Should().BeNull("Notify must never throw, even under concurrent load");

            var delivered = await WaitUntilAsync(
                () => sink.AllDomains().SetEquals(domains),
                FlushTimeout
            );
            delivered.Should().BeTrue("every domain touched under load should be delivered");

            // Let the pipeline quiesce, then assert the coalescing bound.
            await WaitUntilQuiescentAsync(sink);
            sink.Count.Should()
                .BeLessThan(
                    total / 50,
                    "coalescing must collapse the storm into orders-of-magnitude fewer flushes"
                );
        }
        finally
        {
            await coalescer.StopAsync(CancellationToken.None);
        }
    }

    // Waits until the flush count stops growing (two consecutive stable reads), i.e. the coalescer
    // has drained the backlog. Bounded so a stuck pipeline fails the test rather than hanging.
    private static async Task WaitUntilQuiescentAsync(CapturingSink sink)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var last = -1;
        while (DateTime.UtcNow < deadline)
        {
            var now = sink.Count;
            if (now == last)
                return;
            last = now;
            // allowed-delay: poll interval, not a sleep-for-sync. Bounded by the 10s deadline and
            // returns as soon as two consecutive reads match (the backlog is drained).
            await Task.Delay(50);
        }
    }

    [Test]
    public void Notify_WhenChannelFull_DropsWithoutThrowingAndCounts()
    {
        var signal = new TraxChangeSignal(new ChangeSignalOptions { ChannelCapacity = 2 });

        // Nothing is draining, so writes past the capacity are dropped rather than queued.
        var act = () =>
        {
            for (var i = 0; i < 5; i++)
                signal.Notify(ChangeDomain.WorkQueue);
        };

        act.Should().NotThrow("Notify must never throw on a hot write path");
        signal.TotalDropped.Should().Be(3, "two fit in the buffer and three are dropped");
        signal.Dispose();
    }

    [Test]
    public async Task DrainLoop_WhenSinkResolutionThrows_LogsAndKeepsRunning()
    {
        // A throw from outside the per-sink guard (here, resolving the sink itself) must not kill the
        // background loop. The top-level catch in ExecuteAsync logs it and the loop recovers on the
        // next signal. Without that catch the service would die silently on the first hiccup and every
        // later change would be missed.
        var logger = new RecordingLogger<ChangeSignalCoalescer>();
        var realSink = new CapturingSink();
        var shouldThrow = true;

        var services = new ServiceCollection();
        // Transient so the factory runs on every resolution (no singleton caching of the outcome).
        services.AddTransient<IChangeSignalSink>(_ =>
            Volatile.Read(ref shouldThrow)
                ? throw new InvalidOperationException("resolve boom")
                : realSink
        );
        await using var provider = services.BuildServiceProvider();

        var options = FastOptions();
        var signal = new TraxChangeSignal(options);
        var coalescer = new ChangeSignalCoalescer(
            signal,
            provider,
            options,
            TimeProvider.System,
            logger
        );

        await coalescer.StartAsync(CancellationToken.None);
        try
        {
            signal.Notify(ChangeDomain.WorkQueue);
            (await WaitUntilAsync(() => logger.ErrorCount > 0, FlushTimeout))
                .Should()
                .BeTrue("an unexpected drain error must be logged, not fatal to the loop");

            // The loop is still alive: once resolution recovers, a later signal flushes normally.
            Volatile.Write(ref shouldThrow, false);
            signal.Notify(ChangeDomain.Manifest);
            (await WaitUntilAsync(() => realSink.Count == 1, FlushTimeout))
                .Should()
                .BeTrue("the coalescer keeps processing after a recovered error");
            realSink.Flushes[0].Should().ContainSingle().Which.Should().Be(ChangeDomain.Manifest);
        }
        finally
        {
            await coalescer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Complete_DrainsBufferedSignals_ThenCompletesReader()
    {
        // Shutdown contract: Complete() lets already-buffered signals drain, then reports completion
        // so the coalescer's WaitToReadAsync returns false and the loop can exit.
        var signal = new TraxChangeSignal(new ChangeSignalOptions { ChannelCapacity = 8 });
        signal.Notify(ChangeDomain.WorkQueue);
        signal.Notify(ChangeDomain.Manifest);

        signal.Complete();

        var drained = new List<ChangeDomain>();
        while (signal.Reader.TryRead(out var domain))
            drained.Add(domain);
        drained.Should().Equal(ChangeDomain.WorkQueue, ChangeDomain.Manifest);

        (await signal.Reader.WaitToReadAsync())
            .Should()
            .BeFalse("Complete signals that no more entries will arrive");
        signal.Reader.Completion.IsCompleted.Should().BeTrue();
        signal.Dispose();
    }

    [Test]
    public async Task Cancellation_DuringSinkFlush_PropagatesAsCancellation_AndStopsCleanly()
    {
        // If the coalescer is stopped while a sink is mid-flush, the cancellation must surface as
        // cancellation rather than be swallowed by the generic sink guard, so StopAsync unwinds the
        // loop cleanly instead of hanging on the blocked sink.
        var flushEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var blockingSink = new BlockingSink(flushEntered);

        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IChangeSignalSink>(blockingSink);
        await using var provider = services.BuildServiceProvider();

        var options = FastOptions();
        var signal = new TraxChangeSignal(options);
        var coalescer = new ChangeSignalCoalescer(
            signal,
            provider,
            options,
            TimeProvider.System,
            provider.GetService<ILogger<ChangeSignalCoalescer>>()
        );

        await coalescer.StartAsync(CancellationToken.None);
        signal.Notify(ChangeDomain.WorkQueue);

        // Wait until the sink is actually inside FlushAsync (blocked on the token) before stopping.
        // allowed-delay: fail-safe ceiling, not a fixed wait — WhenAny returns the instant the sink
        // enters its flush; the delay only bounds a hang so a regression fails fast instead of hanging.
        var entered = await Task.WhenAny(flushEntered.Task, Task.Delay(FlushTimeout));
        entered.Should().Be(flushEntered.Task, "the sink should reach its flush before we stop");

        // StopAsync cancels the stopping token; the sink's wait throws, the flush rethrows the
        // cancellation, and the loop breaks. This must complete rather than hang or throw.
        var stop = coalescer.StopAsync(CancellationToken.None);
        // allowed-delay: fail-safe ceiling on StopAsync; WhenAny returns the moment it completes.
        var finished = await Task.WhenAny(stop, Task.Delay(FlushTimeout));
        finished
            .Should()
            .Be(stop, "StopAsync must unwind cleanly when a sink is cancelled mid-flush");
        await stop; // observe no exception
    }

    private sealed class BlockingSink(TaskCompletionSource entered) : IChangeSignalSink
    {
        public async Task FlushAsync(
            IReadOnlyCollection<ChangeDomain> domains,
            CancellationToken ct
        )
        {
            entered.TrySetResult();
            // allowed-delay: blocks until the coalescer's stopping token cancels (the mechanism under
            // test), then throws OperationCanceledException with ct.IsCancellationRequested == true.
            await Task.Delay(Timeout.Infinite, ct);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private int _errorCount;
        public int ErrorCount => Volatile.Read(ref _errorCount);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Error)
                Interlocked.Increment(ref _errorCount);
        }
    }
}
