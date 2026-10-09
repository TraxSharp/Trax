using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.InvokedRunListener;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The guarantee that every invoked run's outcome reaches its machine: a hosted service, started on every host that
/// registers machines, that sweeps for the rows whose invoked run has ended and delivers each through
/// <see cref="InvokeOutcomeDelivery"/>. The lifecycle hook is the fast path, on the host that ran the train; a crash
/// between the run's terminal write and its hook, a run the reaper failed, a cancel before dispatch, or a run on a
/// host that registers no machines, all reach the machine through this sweep instead.
/// </summary>
/// <remarks>
/// <para>It sweeps every <see cref="StateMachineOptions.InvokeOutcomeSweepInterval"/>. On Postgres a trigger
/// notifies when an invoked run ends: the notices are drained, deduplicated and checked in one query for the runs
/// that ended on machines this host handles, and those are delivered at once. While it hears the notices it sweeps
/// only every <see cref="StateMachineOptions.InvokeOutcomeSweepIntervalWhileListening"/>, and it sweeps in full
/// whenever it subscribes again after losing them, which covers what ended while it was not listening. Only
/// machines this host registers, and only those that invoke trains, are swept; a host with none does nothing.</para>
/// <para>Any number of hosts may sweep at once. Each delivery is one conditional update on the token, so the
/// outcome is applied by whichever matches first and is a no-transition for every other.</para>
/// <para>A row this host cannot apply an outcome to (<see cref="InvokeDelivery.NotHere"/>: a snapshot it cannot
/// read) is tried again only after a back-off that doubles from <see cref="NotHereBackoff"/> up to
/// <see cref="NotHereBackoffCap"/>, so a row no host can read is logged rarely rather than at every sweep.</para>
/// </remarks>
internal sealed class InvokeOutcomeReconciler(
    IEnumerable<IMachine> machines,
    IServiceScopeFactory scopes,
    StateMachineOptions options,
    ILogger<InvokeOutcomeReconciler> logger,
    IInvokedRunListener? listener = null
) : BackgroundService
{
    /// <summary>How many tokens one page of the sweep, or one batch of notices, reads. Settable for tests.</summary>
    internal int PageSize { get; set; } = 200;

    /// <summary>
    /// How many notices wait to be read at most. A notice that finds the buffer full is dropped, and a full sweep
    /// is asked for instead, which finds its run.
    /// </summary>
    internal const int NoticeCapacity = 1000;

    /// <summary>The first wait before a row this host could not apply an outcome to is tried again.</summary>
    internal static readonly TimeSpan NotHereBackoff = TimeSpan.FromSeconds(30);

    /// <summary>The longest wait before a row this host could not apply an outcome to is tried again.</summary>
    internal static readonly TimeSpan NotHereBackoffCap = TimeSpan.FromHours(1);

    /// <summary>The first wait before subscribing again after the notices were lost; it doubles up to the cap.</summary>
    internal static TimeSpan ResubscribeDelay { get; set; } = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan ResubscribeDelayCap = TimeSpan.FromSeconds(30);

    private readonly Lazy<HashSet<string>> _invoking = new(() =>
        machines
            .OfType<IMachineInternals>()
            .Where(m => m.InvokedTrains.Count > 0)
            .Select(m => ((IMachine)m).Name)
            .ToHashSet(StringComparer.Ordinal)
    );

    private readonly TaskCompletionSource _listening = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    private readonly TaskCompletionSource _firstSweep = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    // The rows this host could not apply an outcome to, by token, and when each may be tried again.
    private readonly ConcurrentDictionary<string, Backoff> _notHere = new(StringComparer.Ordinal);

    // Set when a full sweep is asked for: a resubscription, or a notice dropped from a full buffer.
    private int _sweepAsked;

    // Whether the subscription to the notices is live.
    private volatile bool _hearing;

    /// <summary>Raised after each delivery this reconciler makes, with what it did. For tests.</summary>
    internal event Action<string, InvokeDelivery>? Delivered;

    /// <summary>Raised as the hosted service starts each full sweep. For tests.</summary>
    internal event Action? Sweeping;

    /// <summary>Raised each time the hosted service subscribes to the provider's notices. For tests.</summary>
    internal event Action? Subscribed;

    /// <summary>The clock the back-off of an unreadable row is read on. Settable for tests.</summary>
    internal TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>Completes once the reconciler first hears the provider's notices. For tests.</summary>
    internal Task Listening => _listening.Task;

    /// <summary>Completes once the hosted service's first sweep has ended. For tests.</summary>
    internal Task FirstSweep => _firstSweep.Task;

    /// <summary>Whether <paramref name="machine"/> is registered here and invokes trains.</summary>
    internal bool Handles(string machine) => _invoking.Value.Contains(machine);

    /// <summary>
    /// One full sweep: the rows of the machines this host handles whose invoked run has ended, page by page in
    /// token order, each delivered. Rows whose run is still going are never read past the query that finds the
    /// ended ones. A failure delivering one is logged and the sweep carries on.
    /// </summary>
    internal async Task<IReadOnlyList<InvokeDelivery>> SweepOnce(
        CancellationToken cancellationToken
    )
    {
        var results = new List<InvokeDelivery>();
        if (_invoking.Value.Count == 0)
            return results;

        ForgetExpiredBackoffs();

        string? after = null;
        do
        {
            var page = await EndedPage(after, among: null, cancellationToken);
            await DeliverAll(page.Tokens, results, cancellationToken);
            after = page.Next;
        } while (after is not null);

        return results;
    }

    /// <summary>
    /// Delivers the runs among <paramref name="tokens"/> (each once, however often it is named) that have ended on
    /// a machine this host handles, read in one query per page; the rest cost nothing more.
    /// </summary>
    internal async Task<IReadOnlyList<InvokeDelivery>> DeliverAmong(
        IReadOnlyCollection<string> tokens,
        CancellationToken cancellationToken
    )
    {
        var results = new List<InvokeDelivery>();
        if (_invoking.Value.Count == 0)
            return results;

        foreach (var chunk in tokens.Distinct(StringComparer.Ordinal).Chunk(PageSize))
        {
            var page = await EndedPage(after: null, among: chunk, cancellationToken);
            await DeliverAll(page.Tokens, results, cancellationToken);
        }

        return results;
    }

    private async Task<EndedPage> EndedPage(
        string? after,
        IReadOnlyCollection<string>? among,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<InvokeOutcomeDelivery>()
            .Ended(_invoking.Value, PageSize, after, among, cancellationToken);
    }

    private async Task DeliverAll(
        IReadOnlyList<string> tokens,
        List<InvokeDelivery> results,
        CancellationToken cancellationToken
    )
    {
        foreach (var token in tokens)
            if (!BackingOff(token) && await TryDeliver(token, cancellationToken) is { } delivered)
                results.Add(delivered);
    }

    /// <summary>Delivers the outcome of one run, in a scope of its own. Null when the delivery threw (logged).</summary>
    internal async Task<InvokeDelivery?> TryDeliver(
        string invokeToken,
        CancellationToken cancellationToken
    )
    {
        try
        {
            InvokeDelivery result;
            await using (var scope = scopes.CreateAsyncScope())
                result = await scope
                    .ServiceProvider.GetRequiredService<InvokeOutcomeDelivery>()
                    .Deliver(invokeToken, cancellationToken);
            Record(invokeToken, result);
            Delivered?.Invoke(invokeToken, result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Could not deliver the outcome of invoked run {RunId}; the next sweep tries again.",
                invokeToken
            );
            return null;
        }
    }

    // A row this host cannot apply the outcome to waits longer each time before it is tried again; any other
    // result ends its wait.
    private void Record(string invokeToken, InvokeDelivery result)
    {
        if (result is not InvokeDelivery.NotHere)
        {
            _notHere.TryRemove(invokeToken, out _);
            return;
        }

        var now = Time.GetUtcNow();
        _notHere.AddOrUpdate(
            invokeToken,
            _ => new Backoff(1, now + NotHereBackoff),
            (_, last) =>
            {
                var wait = NotHereBackoff * Math.Pow(2, Math.Min(last.Attempts, 16));
                return new Backoff(
                    last.Attempts + 1,
                    now + (wait > NotHereBackoffCap ? NotHereBackoffCap : wait)
                );
            }
        );
    }

    private bool BackingOff(string invokeToken) =>
        _notHere.TryGetValue(invokeToken, out var backoff) && Time.GetUtcNow() < backoff.Until;

    // A token that is no longer live never comes back, so a wait long past its end is forgotten.
    private void ForgetExpiredBackoffs()
    {
        var stale = Time.GetUtcNow() - 2 * NotHereBackoffCap;
        foreach (var (token, backoff) in _notHere)
            if (backoff.Until < stale)
                _notHere.TryRemove(token, out _);
    }

    private readonly record struct Backoff(int Attempts, DateTimeOffset Until);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_invoking.Value.Count == 0)
            return;

        // A token asks for that one run; a null asks for a full sweep. Bounded: a notice that finds it full is
        // dropped and a full sweep asked for instead (see Notice).
        var wake = Channel.CreateBounded<string?>(
            new BoundedChannelOptions(NoticeCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            }
        );
        var listening = listener is null
            ? Task.CompletedTask
            : Listen(listener, wake.Writer, stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Interlocked.Exchange(ref _sweepAsked, 0);
                Sweeping?.Invoke();
                try
                {
                    await SweepOnce(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "The sweep for invoked runs' outcomes failed; the next one tries again."
                    );
                }
                _firstSweep.TrySetResult();

                await Wait(wake.Reader, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping.
        }

        await listening;
    }

    // Delivers the notices as they come, in deduplicated batches, until the next full sweep is due: the interval
    // has passed (the longer one while the notices are heard), or a full sweep was asked for.
    private async Task Wait(ChannelReader<string?> wake, CancellationToken stoppingToken)
    {
        var since = Stopwatch.GetTimestamp();
        while (true)
        {
            var due =
                (_hearing ? ListeningInterval : options.InvokeOutcomeSweepInterval)
                - Stopwatch.GetElapsedTime(since);
            if (due <= TimeSpan.Zero)
                return;

            // Never waits past the plain interval, so losing the notices brings the sweep back to that pace.
            using var interval = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            interval.CancelAfter(
                due < options.InvokeOutcomeSweepInterval ? due : options.InvokeOutcomeSweepInterval
            );
            try
            {
                await wake.WaitToReadAsync(interval.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                continue;
            }

            var batch = new HashSet<string>(StringComparer.Ordinal);
            var sweep = false;
            while (batch.Count < NoticeCapacity && wake.TryRead(out var notice))
                if (notice is null)
                    sweep = true;
                else
                    batch.Add(notice);

            // A full sweep finds every run the batch names.
            if (sweep || Volatile.Read(ref _sweepAsked) == 1)
                return;

            try
            {
                await DeliverAmong(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Could not read which of {Count} notified invoked runs have ended; the next sweep finds them.",
                    batch.Count
                );
            }
        }
    }

    private TimeSpan ListeningInterval =>
        options.InvokeOutcomeSweepIntervalWhileListening > options.InvokeOutcomeSweepInterval
            ? options.InvokeOutcomeSweepIntervalWhileListening
            : options.InvokeOutcomeSweepInterval;

    // Holds a subscription to the provider's notices for as long as the host runs, resubscribing after a lost
    // connection with a back-off that doubles up to its cap; each resubscription asks for a full sweep, which
    // covers whatever ended while it was not listening.
    private async Task Listen(
        IInvokedRunListener source,
        ChannelWriter<string?> wake,
        CancellationToken stoppingToken
    )
    {
        var first = true;
        var delay = ResubscribeDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var subscription = await source.SubscribeAsync(stoppingToken);
                _hearing = true;
                delay = ResubscribeDelay;
                if (!first)
                    AskForSweep(wake);
                first = false;
                _listening.TrySetResult();
                Subscribed?.Invoke();
                while (true)
                    Notice(wake, await subscription.NextAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _hearing = false;
                logger.LogWarning(
                    ex,
                    "Lost the notice of invoked runs ending; subscribing again in {Delay}. The sweep still "
                        + "delivers every outcome.",
                    delay
                );
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                delay = delay * 2 > ResubscribeDelayCap ? ResubscribeDelayCap : delay * 2;
            }
        }
    }

    // A notice the full buffer cannot take is dropped; the full sweep asked for in its place finds its run, and
    // the full buffer wakes the reader to start it.
    private void Notice(ChannelWriter<string?> wake, string token)
    {
        if (!wake.TryWrite(token))
            Interlocked.Exchange(ref _sweepAsked, 1);
    }

    private void AskForSweep(ChannelWriter<string?> wake)
    {
        Interlocked.Exchange(ref _sweepAsked, 1);
        wake.TryWrite(null);
    }
}

/// <summary>
/// The fast path: on the host that ran an invoked train, delivers its outcome as soon as its terminal write has
/// committed, when this host registers the machine that invoked it. Runs outside the train's execution context, so
/// nothing ambient from the run (its log scope, its caller) reaches the delivery. Never throws: the reconciler's
/// sweep delivers whatever this misses.
/// </summary>
internal sealed class InvokeOutcomeHookFactory(InvokeOutcomeReconciler reconciler)
    : ITrainLifecycleHookFactory
{
    // Long enough for a delivery that queues the next run; a hook never holds the run that called it longer.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    public ITrainLifecycleHook Create() => new Hook(reconciler);

    private sealed class Hook(InvokeOutcomeReconciler reconciler) : ITrainLifecycleHook
    {
        public Task OnCompleted(Metadata metadata, CancellationToken ct) => Deliver(metadata);

        public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
            Deliver(metadata);

        public Task OnCancelled(Metadata metadata, CancellationToken ct) => Deliver(metadata);

        private async Task Deliver(Metadata metadata)
        {
            if (
                metadata.InvokingMachine is not { } machine
                || !reconciler.Handles(machine)
                || string.IsNullOrEmpty(metadata.ExternalId)
            )
                return;

            var token = metadata.ExternalId;
            try
            {
                using var bounded = new CancellationTokenSource(Bound);
                Task delivery;
                using (ExecutionContext.SuppressFlow())
                    delivery = Task.Run(() => reconciler.TryDeliver(token, bounded.Token));
                await delivery;
            }
            catch (Exception)
            {
                // TryDeliver logs its own failures; a bound that ran out leaves the run to the sweep.
            }
        }
    }
}
