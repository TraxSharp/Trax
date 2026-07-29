using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Trax.Effect.Services.ChangeSignal;

namespace Trax.Effect.Tests.Broadcaster.UnitTests;

/// <summary>
/// Window-boundary semantics of <see cref="ChangeSignalCoalescer"/> driven by a controllable clock,
/// so the timing assertions are exact rather than wall-clock-dependent: the coalesce window governs
/// when a flush fires, and everything drained before the window closes lands in one flush.
/// </summary>
[TestFixture]
public class ChangeSignalWindowTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(250);

    private sealed class CapturingSink : IChangeSignalSink
    {
        private readonly object _lock = new();
        public List<ChangeDomain[]> Flushes { get; } = new();

        public Task FlushAsync(IReadOnlyCollection<ChangeDomain> domains, CancellationToken ct)
        {
            lock (_lock)
                Flushes.Add(domains.ToArray());
            return Task.CompletedTask;
        }

        public int Count
        {
            get
            {
                lock (_lock)
                    return Flushes.Count;
            }
        }
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

    // Advance the controlled clock a window at a time until the flush lands. Only one window is ever
    // pending, so this fires exactly one flush; the retry just absorbs the tiny gap between the
    // coalescer draining the buffer and arming the window timer.
    private static async Task AdvanceUntilFlushAsync(
        FakeTimeProvider time,
        CapturingSink sink,
        int targetCount
    )
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (sink.Count < targetCount && DateTime.UtcNow < deadline)
        {
            time.Advance(Window);
            // allowed-delay: poll interval that yields so the background coalescer's continuation
            // runs after the fake clock moves. Bounded by the 10s deadline; the loop exits as soon
            // as sink.Count reaches the target, so a longer delay only slows a failing test.
            await Task.Delay(15);
        }
        sink.Count.Should().Be(targetCount);
    }

    [Test]
    public async Task Window_GovernsFlushTiming_AndCoalescesArrivalsInTheSameWindow()
    {
        var fakeTime = new FakeTimeProvider();
        var options = new ChangeSignalOptions { CoalesceWindow = Window };
        var sink = new CapturingSink();

        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IChangeSignalSink>(sink);
        await using var provider = services.BuildServiceProvider();

        var signal = new TraxChangeSignal(options);
        var coalescer = new ChangeSignalCoalescer(
            signal,
            provider,
            options,
            fakeTime,
            provider.GetService<ILogger<ChangeSignalCoalescer>>()
        );

        await coalescer.StartAsync(CancellationToken.None);
        try
        {
            signal.Notify(ChangeDomain.WorkQueue);
            signal.Notify(ChangeDomain.DeadLetter);

            // Once the buffer is drained the coalescer is parked on the window timer. The clock has
            // not moved, so nothing has flushed yet.
            (await WaitUntilAsync(() => signal.Reader.Count == 0, TimeSpan.FromSeconds(10)))
                .Should()
                .BeTrue();
            sink.Count.Should().Be(0, "the window has not elapsed on the controlled clock");

            // Closing the window produces exactly one flush holding both domains.
            await AdvanceUntilFlushAsync(fakeTime, sink, targetCount: 1);
            sink.Flushes[0]
                .Should()
                .BeEquivalentTo(new[] { ChangeDomain.WorkQueue, ChangeDomain.DeadLetter });

            // A signal after the window closed opens a fresh window: still no flush until we advance.
            signal.Notify(ChangeDomain.Manifest);
            (await WaitUntilAsync(() => signal.Reader.Count == 0, TimeSpan.FromSeconds(10)))
                .Should()
                .BeTrue();
            sink.Count.Should().Be(1, "the new window has not elapsed yet");

            await AdvanceUntilFlushAsync(fakeTime, sink, targetCount: 2);
            sink.Flushes[1].Should().ContainSingle().Which.Should().Be(ChangeDomain.Manifest);
        }
        finally
        {
            await coalescer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task Complete_WhileParkedInWindow_FlushesBufferedDomains_WithoutAdvancingTheClock()
    {
        // Graceful-shutdown path: when the channel completes while the coalescer is parked on an open
        // window, it flushes what it already drained instead of discarding it or waiting for the window
        // to close. The clock is never advanced, so only the completion can trigger the flush here.
        var fakeTime = new FakeTimeProvider();
        var options = new ChangeSignalOptions { CoalesceWindow = Window };
        var sink = new CapturingSink();

        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IChangeSignalSink>(sink);
        await using var provider = services.BuildServiceProvider();

        var signal = new TraxChangeSignal(options);
        var coalescer = new ChangeSignalCoalescer(
            signal,
            provider,
            options,
            fakeTime,
            provider.GetService<ILogger<ChangeSignalCoalescer>>()
        );

        await coalescer.StartAsync(CancellationToken.None);
        try
        {
            signal.Notify(ChangeDomain.WorkQueue);
            signal.Notify(ChangeDomain.DeadLetter);

            // Drained into the open window; parked on the timer that we never fire.
            (await WaitUntilAsync(() => signal.Reader.Count == 0, TimeSpan.FromSeconds(10)))
                .Should()
                .BeTrue();
            sink.Count.Should().Be(0, "the window has not elapsed and the clock never moves");

            signal.Complete();

            (await WaitUntilAsync(() => sink.Count == 1, TimeSpan.FromSeconds(10)))
                .Should()
                .BeTrue("completing the channel drains the open window at once");
            sink.Flushes[0]
                .Should()
                .BeEquivalentTo(new[] { ChangeDomain.WorkQueue, ChangeDomain.DeadLetter });
        }
        finally
        {
            await coalescer.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task SignalArrivingMidWindow_JoinsTheOpenWindow_NotANewOne()
    {
        // A signal that lands after the coalescer has parked on the window timer (but before the window
        // closes) must be drained into the already-open window and flushed with it, not deferred to a
        // second flush. This is the mid-window second-drain path.
        var fakeTime = new FakeTimeProvider();
        var options = new ChangeSignalOptions { CoalesceWindow = Window };
        var sink = new CapturingSink();

        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IChangeSignalSink>(sink);
        await using var provider = services.BuildServiceProvider();

        var signal = new TraxChangeSignal(options);
        var coalescer = new ChangeSignalCoalescer(
            signal,
            provider,
            options,
            fakeTime,
            provider.GetService<ILogger<ChangeSignalCoalescer>>()
        );

        await coalescer.StartAsync(CancellationToken.None);
        try
        {
            signal.Notify(ChangeDomain.WorkQueue);
            // First arrival drained; coalescer is now parked on the window timer.
            (await WaitUntilAsync(() => signal.Reader.Count == 0, TimeSpan.FromSeconds(10)))
                .Should()
                .BeTrue();

            // Second arrival lands mid-window. The clock has not moved, so it can only be picked up by
            // the open window's drain, not by a window expiry.
            signal.Notify(ChangeDomain.DeadLetter);
            (await WaitUntilAsync(() => signal.Reader.Count == 0, TimeSpan.FromSeconds(10)))
                .Should()
                .BeTrue();
            sink.Count.Should().Be(0, "nothing flushes until the single open window closes");

            await AdvanceUntilFlushAsync(fakeTime, sink, targetCount: 1);
            sink.Flushes[0]
                .Should()
                .BeEquivalentTo(
                    new[] { ChangeDomain.WorkQueue, ChangeDomain.DeadLetter },
                    "both arrivals coalesce into the one window"
                );
        }
        finally
        {
            await coalescer.StopAsync(CancellationToken.None);
        }
    }
}
