using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// The derived machines in memory, answering as the host does (ADR <c>Trax.Docs/adr/0058</c>). A trigger or start the
/// host file does not serve the caller is not served; another owner's instance is <c>NOT_FOUND</c>; a caller the rule
/// does not admit is <c>FORBIDDEN</c> with the host's wording; then the engine answers, a guard's message included. The
/// server fires policies on every move and timers as time passes, as the host's runners do.
/// </summary>
internal sealed class MemoryDriver(
    IReadOnlyDictionary<string, JsonObject> irs,
    HostFile host,
    Glossary glossary
) : IScenarioDriver
{
    // The host's own wording (CallerAuthorization, SnapshotDraftService, GenericJunctions).
    internal const string ForbiddenMessage = "You may not do this.";
    internal const string ForbiddenStartMessage = "You may not start this.";
    internal const string NotFoundMessage = "No draft with that id.";

    private const int PolicyDepth = 8;

    private readonly Dictionary<string, IrMachine> _engines = new(StringComparer.Ordinal);
    private readonly Dictionary<InstanceKey, Snapshot> _snapshots = [];
    private readonly Dictionary<InstanceKey, TimeSpan> _entered = [];
    private readonly List<InstanceKey> _order = [];

    // Per instance, the states it entered since the current When began. A Given's arranging is not in it.
    private readonly Dictionary<InstanceKey, List<string>> _cascaded = [];
    private TimeSpan _clock;

    public ValueTask<Answer> Start(Person person, string machine)
    {
        var entry = host.Machines[machine];
        if (entry.Start is null)
            return new(new Answer.NotServed($"no one starts {machine}"));
        if (!Admits(entry.Start, person.Actor))
            return new(new Answer.Refused(RefusalCode.Forbidden, ForbiddenStartMessage));
        var key = new InstanceKey(machine, person.Name);
        return new(new Answer.Moved(Of(key).State));
    }

    public ValueTask Seed(InstanceKey key, string state, JsonObject? context)
    {
        var snapshot = Of(key) with { State = state };
        if (context is not null)
            snapshot = snapshot with { Context = context };
        _snapshots[key] = snapshot;
        _entered[key] = _clock;
        return ValueTask.CompletedTask;
    }

    public ValueTask<(string State, JsonObject Context)?> Read(InstanceKey key) =>
        new(
            _snapshots.TryGetValue(key, out var s)
                ? (s.State, s.Context)
                // The system's instance at its fixed key is there from the start; a keyed one once arranged.
                : (key.Owner is null && key.Key is null ? (Of(key).State, Of(key).Context) : null)
        );

    public ValueTask BeginWhen()
    {
        _cascaded.Clear();
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<string>> Cascaded(InstanceKey key) =>
        new(_cascaded.TryGetValue(key, out var states) ? states.ToList() : []);

    public ValueTask<Answer> Fire(
        Person? person,
        InstanceKey key,
        string trigger,
        JsonObject? input
    )
    {
        if (person is not null)
        {
            var entry = host.Machines[key.Machine];
            if (
                !entry.Triggers.TryGetValue(trigger, out var access)
                || access.Client is not { } rule
            )
                return new(new Answer.NotServed($"no client fires {key.Machine} {trigger}"));
            if (key.Owner is not null && key.Owner != person.Name)
                return new(new Answer.Refused(RefusalCode.NotFound, NotFoundMessage));
            if (key.Owner is not null && !_snapshots.ContainsKey(key))
                return new(new Answer.Refused(RefusalCode.NotFound, NotFoundMessage));
            if (!Admits(rule, person.Actor))
                return new(new Answer.Refused(RefusalCode.Forbidden, ForbiddenMessage));
            // A send: the client gives no input, and the server runs the effect and fires the trigger with its receipt,
            // here the glossary's example, as derivation reads it.
            if (
                glossary
                    .Machines.GetValueOrDefault(key.Machine)
                    ?.Triggers.GetValueOrDefault(trigger)
                    ?.Effect is
                { } effect
            )
                input = new JsonObject { [Glossary.ReceiptField] = effect.Example };
        }
        return new(Moved(key, Engine(key.Machine).Advance(Of(key), trigger, input)));
    }

    public ValueTask<Answer> Outcome(
        InstanceKey key,
        string state,
        string kind,
        JsonNode? output
    ) => new(Moved(key, Engine(key.Machine).ApplyOutcome(Of(key), $"{state}.{kind}", output)));

    // Every timer due by the new time fires, earliest first, and a timer its target starts fires too when it is due.
    public ValueTask Pass(TimeSpan time)
    {
        if (_clock > TimeSpan.MaxValue - time)
            throw new InvalidOperationException(
                $"Waiting {time} more adds up to more time than a clock holds ({TimeSpan.MaxValue.Days} days)."
            );
        _clock += time;
        for (var fired = 0; fired < 1000; fired++)
        {
            var due = _order
                .Select(key =>
                    (
                        Key: key,
                        At: TimerOf(key, _snapshots[key].State) is { } after
                            ? _entered[key] + after
                            : (TimeSpan?)null
                    )
                )
                .Where(d => d.At is { } at && at <= _clock)
                .OrderBy(d => d.At)
                .ThenBy(d => d.Key.Machine, StringComparer.Ordinal)
                .ThenBy(d => d.Key.Owner, StringComparer.Ordinal)
                .ThenBy(d => d.Key.Key, StringComparer.Ordinal)
                .FirstOrDefault();
            if (due.At is not { } fireAt)
                break;
            var snapshot = _snapshots[due.Key];
            if (
                Engine(due.Key.Machine).ApplyTimer(snapshot, $"{snapshot.State}.after")
                is not AdvanceResult.Transitioned timed
            )
                break;
            Enter(due.Key, timed.Snapshot, fireAt);
            React(due.Key, timed.Snapshot.State, 0);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<(IReadOnlyList<string>? Values, Answer? Refused)> View(
        Person person,
        string view,
        string field
    )
    {
        if (!host.Views.TryGetValue(view, out var served))
            return new((null, new Answer.NotServed($"no view {view}")));
        if (!Admits(served.Readers, person.Actor))
            return new((null, new Answer.Refused(RefusalCode.Forbidden, ForbiddenMessage)));
        foreach (var read in served.Reads.Where(r => r.Key is not null))
            Of(new InstanceKey(read.Machine, null));
        return new(
            (
                ViewValues.Shown(
                    glossary.Views[view],
                    field,
                    person.Name,
                    _snapshots.Select(kv => (kv.Key, (string?)kv.Value.State, kv.Value.Context))
                ),
                null
            )
        );
    }

    private Answer Moved(InstanceKey key, AdvanceResult result)
    {
        switch (result)
        {
            case AdvanceResult.Transitioned moved:
                Enter(key, moved.Snapshot, _clock);
                React(key, moved.Snapshot.State, 0);
                return new Answer.Moved(moved.Snapshot.State);
            case AdvanceResult.Rejected refused:
                return new Answer.Refused(
                    refused.Reason,
                    refused.Detail ?? refused.Reason.ToWire()
                );
            default:
                throw new InvalidOperationException($"Unexpected answer {result}.");
        }
    }

    // The policies an instance entering a state sets off, fired as the server fires them: a broadcast on every
    // instance waiting in its state, a system policy on the system instance, a linked one on the same owner's.
    private void React(InstanceKey source, string entered, int depth)
    {
        if (depth > PolicyDepth)
            return;
        foreach (
            var policy in host.Policies.Where(p => p.When == source.Machine && p.Enters == entered)
        )
        {
            if (policy.Shape == "toSystem")
                Of(new InstanceKey(policy.Target, null));
            var reached = _order
                .Where(key =>
                    key.Machine == policy.Target
                    && policy.Shape switch
                    {
                        "broadcast" => _snapshots[key].State == policy.By,
                        "toSystem" => key.Owner is null,
                        _ => key.Owner == source.Owner,
                    }
                )
                .ToList();
            foreach (var key in reached)
                if (
                    Engine(policy.Target).Advance(_snapshots[key], policy.Trigger)
                    is AdvanceResult.Transitioned fired
                )
                {
                    Enter(key, fired.Snapshot, _clock);
                    React(key, fired.Snapshot.State, depth + 1);
                }
        }
    }

    private Snapshot Of(InstanceKey key)
    {
        if (_snapshots.TryGetValue(key, out var snapshot))
            return snapshot;
        snapshot = Engine(key.Machine).CreateInitialSnapshot();
        _snapshots[key] = snapshot;
        _entered[key] = _clock;
        _order.Add(key);
        return snapshot;
    }

    // A move: a trigger, an outcome, a timer or a policy. A Given's arranging goes around it.
    private void Enter(InstanceKey key, Snapshot snapshot, TimeSpan at)
    {
        _snapshots[key] = snapshot;
        _entered[key] = at;
        if (!_cascaded.TryGetValue(key, out var states))
            _cascaded[key] = states = [];
        states.Add(snapshot.State);
    }

    private IrMachine Engine(string machine)
    {
        if (!_engines.TryGetValue(machine, out var engine))
            _engines[machine] = engine = new IrMachine(
                irs[machine],
                glossary.Machines.GetValueOrDefault(machine)?.ExampleRules,
                glossary.Machines.GetValueOrDefault(machine)?.ExampleReductions
            );
        return engine;
    }

    private TimeSpan? TimerOf(InstanceKey key, string state) =>
        irs[key.Machine]["timers"] is JsonArray timers
        && timers.FirstOrDefault(t => t!["state"]!.GetValue<string>() == state) is { } timer
        && IsoDuration.TryParse(timer["after"]!.GetValue<string>(), out var after)
            ? after
            : null;

    // As CallerAuthorization reads a rule: every role set met by one of the actor's roles, or any signed-in caller.
    private static bool Admits(AccessRule rule, GlossaryActor actor) =>
        rule.IsAnyAuthenticated
        || (!actor.AnyAuthenticated && rule.RoleSets.All(set => set.Any(actor.Roles.Contains)));
}
