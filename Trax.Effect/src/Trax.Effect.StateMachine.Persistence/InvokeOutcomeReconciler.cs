using System.Diagnostics.CodeAnalysis;
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
/// registers machines, that sweeps the rows holding a live invoke token, finds those whose run has ended, and
/// delivers each through <see cref="InvokeOutcomeDelivery"/>. The lifecycle hook is the fast path, on the host that
/// ran the train; a crash between the run's terminal write and its hook, a run the reaper failed, a cancel before
/// dispatch, or a run on a host that registers no machines, all reach the machine through this sweep instead.
/// </summary>
/// <remarks>
/// <para>It sweeps every <see cref="StateMachineOptions.InvokeOutcomeSweepInterval"/>, and on Postgres sooner: a
/// trigger notifies when an invoked run ends, and the notice's run is delivered at once. Only machines this host
/// registers, and only those that invoke trains, are swept; a host with none does nothing.</para>
/// <para>Any number of hosts may sweep at once. Each delivery is one conditional update on the token, so the
/// outcome is applied by whichever matches first and is a no-transition for every other.</para>
/// </remarks>
internal sealed class InvokeOutcomeReconciler(
    IEnumerable<IMachine> machines,
    IServiceScopeFactory scopes,
    StateMachineOptions options,
    ILogger<InvokeOutcomeReconciler> logger,
    IInvokedRunListener? listener = null
) : BackgroundService
{
    /// <summary>How many live tokens one page of the sweep reads.</summary>
    internal const int PageSize = 200;

    private static readonly TimeSpan ResubscribeDelay = TimeSpan.FromSeconds(5);

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

    /// <summary>Raised after each delivery this reconciler makes, with what it did. For tests.</summary>
    internal event Action<string, InvokeDelivery>? Delivered;

    private readonly TaskCompletionSource _firstSweep = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );

    /// <summary>Completes once the reconciler first hears the provider's notices. For tests.</summary>
    internal Task Listening => _listening.Task;

    /// <summary>Completes once the hosted service's first sweep has ended. For tests.</summary>
    internal Task FirstSweep => _firstSweep.Task;

    /// <summary>Whether <paramref name="machine"/> is registered here and invokes trains.</summary>
    internal bool Handles(string machine) => _invoking.Value.Contains(machine);

    /// <summary>
    /// One full sweep: every row holding a live token for a machine this host handles, page by page, delivering
    /// each whose run has ended. A failure delivering one is logged and the sweep carries on.
    /// </summary>
    internal async Task<IReadOnlyList<InvokeDelivery>> SweepOnce(
        CancellationToken cancellationToken
    )
    {
        var results = new List<InvokeDelivery>();
        if (_invoking.Value.Count == 0)
            return results;

        string? after = null;
        while (true)
        {
            IReadOnlyList<InvokingInstance> page;
            IReadOnlyList<string> ended;
            await using (var scope = scopes.CreateAsyncScope())
            {
                page = await scope
                    .ServiceProvider.GetRequiredService<IMachineInstanceStore>()
                    .ListInvoking(PageSize, after, cancellationToken);
                if (page.Count == 0)
                    break;
                after = page[^1].InvokeToken;

                var mine = page.Where(p => Handles(p.Machine)).Select(p => p.InvokeToken).ToList();
                ended =
                    mine.Count == 0
                        ? []
                        : await scope
                            .ServiceProvider.GetRequiredService<InvokeOutcomeDelivery>()
                            .EndedAmong(mine, cancellationToken);
            }

            foreach (var token in ended)
                if (await TryDeliver(token, cancellationToken) is { } delivered)
                    results.Add(delivered);

            if (page.Count < PageSize)
                break;
        }

        return results;
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

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_invoking.Value.Count == 0)
            return;

        // A null item asks for a full sweep; a token asks for that one run.
        var wake = Channel.CreateUnbounded<string?>(new UnboundedChannelOptions());
        var listening = listener is null
            ? Task.CompletedTask
            : Listen(listener, wake.Writer, stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
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

                using var interval = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                interval.CancelAfter(options.InvokeOutcomeSweepInterval);
                try
                {
                    while (await wake.Reader.ReadAsync(interval.Token) is { } token)
                        await TryDeliver(token, stoppingToken);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // The interval elapsed: sweep again.
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping.
        }

        await listening;
    }

    // Holds a subscription to the provider's notices for as long as the host runs, resubscribing after a lost
    // connection; each resubscription asks for a full sweep, which covers whatever ended while it was not listening.
    private async Task Listen(
        IInvokedRunListener source,
        ChannelWriter<string?> wake,
        CancellationToken stoppingToken
    )
    {
        var first = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var subscription = await source.SubscribeAsync(stoppingToken);
                if (!first)
                    wake.TryWrite(null);
                first = false;
                _listening.TrySetResult();
                while (true)
                    wake.TryWrite(await subscription.NextAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Lost the notice of invoked runs ending; resubscribing. The sweep still delivers every outcome."
                );
                try
                {
                    await Task.Delay(ResubscribeDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
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
