using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Configuration.ClientExport;
using Trax.Effect.Data.Services.SqlDialect;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Refuses to start a host whose machines spawn a child they cannot start or hear from. Every spawning state is
/// checked once, before any hosted service starts: the child is a machine this host registers, owned the way its
/// parent is (a user-owned parent's child is its user's, a system parent's the system's), it declares where it ends
/// (<c>Ends()</c> or <c>Fails()</c>), and its initial state neither invokes a train nor spawns a child, since a
/// spawned child is inserted in its initial state and nothing would start that work. Drafts are stored on a provider
/// with transactions (not InMemory), so the child and the parent's move are one write. A user-owned parent may not
/// enter a spawning state through an outcome or a timer, with no user present. See
/// <c>Trax.Docs/adr/0052-a-state-spawns-a-child-machine-as-it-invokes-a-train.md</c>.
/// </summary>
internal sealed class SpawnsStartupValidator(
    IEnumerable<IMachine> machines,
    IServiceScopeFactory scopeFactory
) : IHostedLifecycleService, ITraxStartupCheck
{
    private bool _checked;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;
        await using var scope = scopeFactory.CreateAsyncScope();
        var registered = machines.ToList();
        var problems = registered
            .SelectMany(m => Problems(m, registered, scope.ServiceProvider))
            .ToList();
        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"{problems.Count} problem(s) stop this host's state machines from spawning their children:"
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
    /// What stops <paramref name="machine"/>'s spawning states from starting their children among the
    /// <paramref name="registered"/> machines on the host <paramref name="services"/> describes, one sentence per
    /// problem; empty when nothing does.
    /// </summary>
    internal static IReadOnlyList<string> Problems(
        IMachine machine,
        IReadOnlyCollection<IMachine> registered,
        IServiceProvider services
    )
    {
        if (machine is not IMachineInternals { Spawns: { Count: > 0 } spawns } internals)
            return [];

        var problems = new List<string>();
        var name = machine.Name;
        var spawning = spawns.Select(s => s.State).ToHashSet(StringComparer.Ordinal);

        if (services.GetService<ISqlDialect>() is null)
            problems.Add(
                $"The machine '{name}' spawns child machines, but the data provider is InMemory, which has no "
                    + "transactions: a child must be created in the transaction that moves its parent. Use "
                    + "UsePostgres(...) or UseSqlite(...)."
            );

        if (!internals.SystemOwned)
        {
            foreach (
                var chained in internals.ChainedOutcomes.Where(c => spawning.Contains(c.Target))
            )
                problems.Add(
                    $"The machine '{name}' is user-owned, and the {chained.Outcome} outcome of {chained.State} "
                        + $"enters {chained.Target}, which spawns a child. The child would be started with no user "
                        + $"present. Enter {chained.Target} through an event the user sends, or declare the machine "
                        + "SystemOwned()."
                );
            foreach (
                var (timed, target) in internals.TimerTargets.OrderBy(
                    t => t.Key,
                    StringComparer.Ordinal
                )
            )
                if (spawning.Contains(target))
                    problems.Add(
                        $"The machine '{name}' is user-owned, and the timer of {timed} enters {target}, which spawns "
                            + "a child. The child would be started by the server with no user present. Let an event "
                            + "the user sends enter it, or declare the machine SystemOwned()."
                    );
        }

        foreach (var spawn in spawns)
        {
            var at = $"The machine '{name}' state {spawn.State} spawns {spawn.ChildMachine.Name}";
            if (
                registered.FirstOrDefault(m => m.GetType() == spawn.ChildMachine)
                is not IMachineInternals child
            )
            {
                problems.Add(
                    $"{at}, which this host does not register as a machine. Register it with AddStateMachines, "
                        + "in an assembly the host scans."
                );
                continue;
            }

            if (child.SystemOwned != spawn.SystemOwned)
                problems.Add(
                    spawn.SystemOwned
                        ? $"{at}, which is user-owned, but the parent is system-owned, so the child would have no "
                            + "user. Declare the child SystemOwned()."
                        : $"{at}, which is system-owned, but the parent is user-owned, and a user-owned parent's "
                            + "child is its user's. Remove SystemOwned() from the child, or spawn another machine."
                );

            if (child.Ends.Count == 0)
                problems.Add(
                    $"{at}, which declares no state it ends in, so the parent would wait for good. Mark the child's "
                        + "final states with Ends() or Fails()."
                );

            string initial;
            try
            {
                initial = child.InitialSnapshot(null).State;
            }
            catch (ArgumentException ex)
            {
                problems.Add($"{at}, whose declared initial context is not valid: {ex.Message}");
                continue;
            }
            if (child.Entering(initial) is { } work)
                problems.Add(
                    $"{at}, whose initial state {initial} {(work.Spawn is null ? "invokes a train" : "spawns a child")}. "
                        + "A spawned child is created in its initial state with nothing started there; give it a "
                        + "first state that waits for an event."
                );
        }

        return problems;
    }
}
