using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Attributes;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Refuses to start a host that serves a machine to clients without a rule for everything a client can do to it. A
/// machine a client can reach is one marked <see cref="Trax.Effect.Attributes.TraxMachineAttribute"/>, however it was
/// registered, or, while the generic mutations are opted in, every user-owned machine. Each must declare a rule for
/// every client and effect-bound event, for its <c>start</c>, <c>get</c>, <c>machineChanged</c> and <c>discard</c>; none of its events or
/// its start may be <c>Anonymous()</c>, since a user's instance needs an owner; a read or the discard may not be looser
/// than owner-only; and every
/// policy a rule names must be registered. A marked machine must also have an IR (no raw delegates, no delegate
/// guard or reducer beside declarative ones) and be user-owned. A machine nothing serves needs no rules. Every
/// problem of every machine is listed in one refusal.
/// </summary>
/// <remarks>
/// It runs in <see cref="StartingAsync"/>, before any hosted service starts, as the invoke startup check does.
/// </remarks>
internal sealed class MachineRulesStartupValidator(
    IEnumerable<IMachine> machines,
    StateMachineOptions options,
    IServiceScopeFactory scopeFactory
) : IHostedLifecycleService
{
    private bool _checked;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        _checked = true;
        await using var scope = scopeFactory.CreateAsyncScope();
        var caller = scope.ServiceProvider.GetService<ICallerAuthorization>();
        var problems = await Problems(
            RegisteredMachines.Distinct(machines).ToList(),
            options,
            caller
        );
        if (problems.Count > 0)
            throw new InvalidOperationException(
                $"{problems.Count} problem(s) stop this host from serving its state machines to clients:"
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

    /// <summary>Every problem with the machines clients can reach, one sentence each; empty when there is none.</summary>
    internal static async Task<IReadOnlyList<string>> Problems(
        IReadOnlyList<IMachine> machines,
        StateMachineOptions options,
        ICallerAuthorization? caller
    )
    {
        var problems = new List<string>();

        // An opt-in without a policy is refused earlier, where AddStateMachines registers the generic mutations.

        foreach (var machine in machines)
        {
            var type = machine.GetType();
            var exposed = StateMachineOptions.IsServed(machine);
            var internals = machine as IMachineInternals;
            var systemOwned = internals?.SystemOwned ?? false;

            // The attribute is not inherited, so a subclass of a marked machine is not served by it. One left unmarked
            // refuses the host rather than leave its author believing it is served.
            if (!exposed && MarkedBase(type) is { } marked)
                problems.Add(
                    $"The machine '{machine.Name}' ({type.FullName}) derives from {marked.Name}, which is marked "
                        + "[TraxMachine], but is not marked itself, and the attribute is not inherited, so it is not "
                        + "served. Mark it [TraxMachine]."
                );

            var optedIn = options.GenericMutationsExposed && !systemOwned;
            if (!exposed && !optedIn)
                continue;

            var name = machine.Name;
            var reach = exposed
                ? "marked [TraxMachine]"
                : "reachable through the generic stateMachine mutations";
            // How to stop serving it: the attribute is the only thing that serves it.
            const string unserve = "remove [TraxMachine] from it";

            if (internals is null)
            {
                problems.Add(
                    $"The machine '{name}' is {reach}, but it does not derive from Machine<TState, TTrigger>, so "
                        + "it declares no rules to check its callers against."
                );
                continue;
            }

            if (exposed)
            {
                if (machine.SchemaHash is null)
                {
                    // A machine mixing delegates with rules has no IR either; naming its delegate events says
                    // which declarations stand in the way.
                    var delegates =
                        internals.DelegateTriggers.Count > 0
                            ? " (its event(s) "
                                + string.Join(
                                    ", ",
                                    internals.DelegateTriggers.Order(StringComparer.Ordinal)
                                )
                                + " use a delegate guard or reducer)"
                            : "";
                    problems.Add(
                        $"The machine '{name}' is {reach}, but it is built from delegates and has no IR{delegates}, "
                            + "which its typed operations and their version are built from. Declare it with Context, "
                            + "When and Reduce, or "
                            + unserve
                            + "."
                    );
                    continue;
                }
                if (systemOwned)
                {
                    problems.Add(
                        $"The machine '{name}' is {reach}, but it is system-owned: only host code creates and "
                            + "drives its instances, so it has no client surface. "
                            + char.ToUpperInvariant(unserve[0])
                            + unserve[1..]
                            + "."
                    );
                    continue;
                }
                if (internals.DelegateTriggers.Count > 0)
                    problems.Add(
                        $"The machine '{name}' is {reach}, but its event(s) "
                            + string.Join(
                                ", ",
                                internals.DelegateTriggers.Order(StringComparer.Ordinal)
                            )
                            + " use a delegate guard or reducer, which its IR and version do not describe, so a "
                            + "change to one would never make a client outdated. Declare them with When(Rule) and "
                            + "Reduce(Reduction)."
                    );
            }

            problems.AddRange(await RuleProblems(machine, internals, reach, caller));
        }

        return problems;
    }

    // The nearest base type of a machine that carries the attribute itself, or null.
    private static Type? MarkedBase(Type machine)
    {
        for (var type = machine.BaseType; type is not null; type = type.BaseType)
            if (type.IsDefined(typeof(TraxMachineAttribute), inherit: false))
                return type;
        return null;
    }

    private static async Task<IEnumerable<string>> RuleProblems(
        IMachine machine,
        IMachineInternals internals,
        string reach,
        ICallerAuthorization? caller
    )
    {
        var problems = new List<string>();
        var name = machine.Name;
        var rules = internals.Rules;

        var events = machine
            .Triggers.Where(t => t.Kind is TriggerKind.Client or TriggerKind.EffectBound)
            .Select(t => t.Name)
            .ToList();
        var undeclared = events.Where(e => !rules.Events.ContainsKey(e)).ToList();
        if (rules.Start is null)
            undeclared.Add("start");
        if (rules.Get is null)
            undeclared.Add("get");
        if (rules.Watch is null)
            undeclared.Add("machineChanged");
        if (rules.Discard is null)
            undeclared.Add("discard");
        if (undeclared.Count > 0)
            problems.Add(
                $"The machine '{name}' is {reach}, and declares no rule for {string.Join(", ", undeclared)}. "
                    + "Every event a client can send declares Requires(...), RequiresPolicy(...), AnyAuthenticated() "
                    + "or Anonymous(), and start, get, machineChanged and discard declare theirs with Start(...), "
                    + "Get(...), Watch(...) and Discard(...). There is no machine-level default."
            );

        var anonymous = rules
            .Events.Where(e => e.Value.Kind == CallerRuleKind.Anonymous)
            .Select(e => e.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (rules.Start is { Kind: CallerRuleKind.Anonymous })
            anonymous.Add("start");
        if (anonymous.Count > 0)
            problems.Add(
                $"The machine '{name}' is user-owned and {reach}, but {string.Join(", ", anonymous)} "
                    + "declare(s) Anonymous(). A user's instance belongs to an authenticated user, so an anonymous "
                    + "caller has nothing to own it by. Use AnyAuthenticated() or a requirement."
            );

        foreach (
            var (read, rule) in new[]
            {
                ("get", rules.Get),
                ("machineChanged", rules.Watch),
                ("discard", rules.Discard),
            }
        )
            if (rule is { Kind: CallerRuleKind.Anonymous or CallerRuleKind.AnyAuthenticated })
                problems.Add(
                    $"The machine '{name}' is {reach}, and its {read} rule is {rule}, looser than owner-only. "
                        + "Owner-only is the floor for reading or discarding an instance: declare OwnerOnly(), and "
                        + "Requires(...) or RequiresPolicy(...) beside it to narrow it further."
                );

        foreach (
            var (where, policy) in rules
                .All()
                .SelectMany(r => r.Rule.Policies.Select(p => (r.Where, Policy: p)))
                .Distinct()
        )
            if (caller is null || !await caller.IsPolicyRegistered(policy))
                problems.Add(
                    $"The machine '{name}' is {reach}, and {where} requires the policy '{policy}', which this host "
                        + (
                            caller is null
                                ? "cannot evaluate: no ICallerAuthorization is registered (Trax.Api registers one). "
                                : "does not register. "
                        )
                        + "No caller could ever satisfy it. Register the policy, or name one that is."
                );

        return problems;
    }
}
