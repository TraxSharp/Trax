using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Services.JunctionEffectProviderFactory;
using Trax.Effect.Utils;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Refuses to start a host whose machines invoke a train it cannot queue, cancel or authorize as declared. Every
/// state that invokes a train is checked once, before any hosted service starts: the host registers Trax.Mediator
/// (the launcher), stores drafts on a provider with transactions (not InMemory), registers the cancel-flag check that
/// lets leaving a state cancel its run from any host (<c>AddJunctionProgress</c>), and the train's output reaches no
/// <c>[TraxSensitive]</c> member, because the output is reduced into a context stored as plain JSON. A user-owned
/// machine may not chain runs through outcomes: no outcome of an invoking state enters another invoking state,
/// since that run would be queued with no user present to authorize it. The launcher then
/// checks the train itself: that it is a registered <c>ServiceTrain</c> built only from effect junctions, with no
/// deferred promotion or <c>OnQueue</c> hook, and authorized compatibly with the machine's owner.
/// </summary>
/// <remarks>
/// It runs in <see cref="StartingAsync"/>, which the host finishes for every hosted service before it calls any
/// <c>StartAsync</c>, so a refused host never starts a dispatcher. Something that starts hosted services itself and
/// calls only <c>StartAsync</c> gets the check from there.
/// </remarks>
[Experimental(ExperimentalIds.Invokes)]
internal sealed class InvokesStartupValidator(
    IEnumerable<IMachine> machines,
    IServiceScopeFactory scopeFactory
) : IHostedLifecycleService
{
    private bool _checked;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;
        await using var scope = scopeFactory.CreateAsyncScope();
        var problems = machines.SelectMany(m => Problems(m, scope.ServiceProvider)).ToList();
        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"{problems.Count} problem(s) stop this host's state machines from invoking their trains:"
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, problems.Select(p => "  - " + p))
            );
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        _checked ? Task.CompletedTask : StartingAsync(cancellationToken);

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// What stops <paramref name="machine"/>'s invoking states from running their trains on the host
    /// <paramref name="services"/> describes, one sentence per problem; empty when nothing does.
    /// </summary>
    internal static IReadOnlyList<string> Problems(IMachine machine, IServiceProvider services)
    {
        if (
            machine
            is not IMachineInternals { InvokedTrains: { Count: > 0 } declarations } internals
        )
            return [];

        var problems = new List<string>();
        var name = machine.Name;

        // An outcome that enters another invoking state queues the next run with no user present, so it could be
        // authorized only in Trax's trusted scope. A user owns this machine's instances, and their runs are
        // authorized against them, so the chain goes through an event the user sends instead.
        if (!internals.SystemOwned)
            foreach (var chained in internals.ChainedOutcomes)
                problems.Add(
                    $"The machine '{name}' is user-owned, and the {chained.Outcome} outcome of {chained.State} "
                        + $"enters {chained.Target}, which invokes a train of its own. Its run would be queued "
                        + "with no user present to authorize it. Chain through an event the user sends (a "
                        + $"continue or retry edge into {chained.Target}), or declare the machine SystemOwned()."
                );

        var launcher = services.GetService<IInvokedTrainLauncher>();
        if (launcher is null)
            problems.Add(
                $"The machine '{name}' invokes trains, but Trax.Mediator is not registered, so nothing can queue "
                    + "their runs. Call AddMediator(...) after AddStateMachines(...)."
            );

        // InMemory registers no SQL dialect, and has no transactions to queue a run in with its advance.
        if (services.GetService<ISqlDialect>() is null)
            problems.Add(
                $"The machine '{name}' invokes trains, but the data provider is InMemory, which has no "
                    + "transactions: a run must be queued in the transaction that moves the snapshot. Use "
                    + "UsePostgres(...) or UseSqlite(...)."
            );

        if (
            !services
                .GetServices<IJunctionEffectProviderFactory>()
                .Any(f => f is ICancellationFlagCheckFactory)
        )
            problems.Add(
                $"The machine '{name}' invokes trains, but this host does not check a run's cancel flag between "
                    + "junctions, so leaving an invoking state could not stop its run on another host. Call "
                    + "AddJunctionProgress() in AddEffects(...)."
            );

        foreach (var declaration in declarations)
        {
            if (TraxRedaction.ReachesSensitiveMember(declaration.OutputType))
                problems.Add(
                    $"The machine '{name}' invokes {declaration.TrainType.Name} in {declaration.State}, whose output "
                        + $"{declaration.OutputType.Name} reaches a [TraxSensitive] member. The output is reduced "
                        + "into the snapshot's context, which is stored as plain JSON and returned by loadSnapshot, "
                        + "so it cannot hold a sensitive value. Return a pointer to it instead."
                );

            if (launcher is not null)
                problems.AddRange(launcher.Refusals(declaration, services));
        }

        return problems;
    }
}
