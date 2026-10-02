using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Core.Decisions;
using Trax.Core.Monad;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Scheduler.Services.DecisionRecording;

/// <summary>
/// Refuses to start a host that runs trains asking a decider when it does not record decisions
/// (<c>AddDecisionRecording</c>), naming every such train.
/// </summary>
/// <remarks>
/// Such a host could run those trains and ask their deciders. What it cannot do is run a requeue
/// that replays a recorded run's decisions: it has no recorded answers to replay, so the run
/// fails, classified permanent, rather than asking afresh and perhaps taking another track
/// (central <c>docs/0041</c>). A requeue links a run only when the run has decisions to replay, so
/// this happens where the run was recorded by one host and the requeue is run by another that does
/// not record: one worker of a fleet left without the call.
///
/// <para>A refusal rather than a warning, because Trax fails closed. A host that runs deciding
/// trains without recording neither keeps the record a requeue replays nor can replay one it is
/// handed, and that gap would otherwise surface only when a requeue fails in production, on
/// whichever worker happened to claim it. A warning at startup is easy to miss across a fleet;
/// a host that will not start is not.</para>
///
/// <para>A train's chain is read the way the mediator's chain verification reads it, the chain
/// itself and every track it routes to. A train that cannot be built or read outside a request is
/// left out: the mediator's chain verification already reports it. Every train is checked before
/// the host is refused, so one start names all of them.</para>
///
/// <para>The check runs in <see cref="StartingAsync"/>, which the host finishes for every hosted
/// service before it calls any <c>StartAsync</c>, so a refusal stops the host before a worker
/// starts claiming work, even under <c>HostOptions.ServicesStartConcurrently</c>. It is also
/// registered first, as the mediator's startup gates are, so a host that starts its services one
/// after another reaches it before anyone else's lifecycle hook. Something that starts hosted
/// services itself and calls only <c>StartAsync</c> gets the check from there instead.</para>
/// </remarks>
internal sealed class DecisionRecordingStartupCheck(
    IServiceScopeFactory scopeFactory,
    IServiceProviderIsService isService,
    // Optional: AddTraxJobRunner registers this check, and a host built without AddMediator has
    // no trains to check, which the mediator's own validation reports.
    ITrainDiscoveryService? discoveryService = null
) : IHostedLifecycleService
{
    private bool _checked;

    /// <summary>
    /// Registers the check first among the hosted services, once however many of
    /// <c>AddScheduler</c>, <c>AddTraxJobRunner</c> and <c>AddTraxWorker</c> ask for it.
    /// </summary>
    internal static void Register(IServiceCollection services)
    {
        if (
            services.Any(d =>
                d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(DecisionRecordingStartupCheck)
            )
        )
            return;

        services.Insert(
            0,
            ServiceDescriptor.Singleton<IHostedService, DecisionRecordingStartupCheck>()
        );
    }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;

        if (discoveryService is null || isService.IsService(typeof(IDecisionReplay)))
            return;

        var deciding = await TrainsThatDecideAsync(cancellationToken);

        if (deciding.Count == 0)
            return;

        throw new InvalidOperationException(
            $"{deciding.Count} registered {(deciding.Count == 1 ? "train asks" : "trains ask")} a "
                + "decider, but this host does not record decisions:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, deciding.Select(train => "  - " + train))
                + Environment.NewLine
                + "Without the record, a requeue of a run whose decisions were recorded fails here, "
                + "permanently, rather than replaying them. Call AddDecisionRecording() on the "
                + "effects builder of every host that runs these trains."
        );
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _checked ? Task.CompletedTask : StartingAsync(cancellationToken);

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The names of the registered trains whose chains ask a decider, in order.</summary>
    internal async Task<IReadOnlyList<string>> TrainsThatDecideAsync(
        CancellationToken cancellationToken
    )
    {
        // Async, as the mediator's check does: a train's scoped dependency may implement only
        // IAsyncDisposable.
        await using var scope = scopeFactory.CreateAsyncScope();
        var deciding = new List<string>();

        foreach (var registration in discoveryService?.DiscoverTrains() ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (DeclaredChain(scope.ServiceProvider, registration) is { } chain && Decides(chain))
                deciding.Add(registration.ServiceType.FullName ?? registration.ServiceTypeName);
        }

        deciding.Sort(StringComparer.Ordinal);
        return deciding;
    }

    private static ChainRecorder? DeclaredChain(
        IServiceProvider services,
        TrainRegistration registration
    )
    {
        try
        {
            var train = services.GetRequiredService(registration.ServiceType);

            return train
                    .GetType()
                    .GetMethod(nameof(Core.Train.Train<,>.DeclaredChain), Type.EmptyTypes)
                    ?.Invoke(train, null) as ChainRecorder;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Whether the chain, or any track it routes to, asks a decider.</summary>
    private static bool Decides(ChainRecorder chain)
    {
        for (var i = 0; i < chain.Steps.Count; i++)
        {
            if (chain.Steps[i].Kind == ChainStepKind.Decide)
                return true;

            if (chain.TracksAt(i).Any(track => Decides(track.Steps)))
                return true;
        }

        return false;
    }
}
