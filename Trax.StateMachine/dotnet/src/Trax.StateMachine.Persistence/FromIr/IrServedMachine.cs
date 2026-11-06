using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Persistence.FromIr;

/// <summary>
/// A machine served from its IR file and its entry in the host file, with no C# class of its own (ADR
/// <c>Trax.Docs/adr/0058</c>). It is closed over the enums <see cref="IrTypes"/> emitted for it, and its
/// <see cref="Configure"/> replays the IR into the same builder a hand-written machine declares itself with, so from
/// there it is served like any other. <c>IrMachines.Create</c> builds one and refuses it if what it exports is not
/// the file.
/// </summary>
internal abstract class IrServedMachine<TState, TTrigger>(IrReplay replay)
    : Machine<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <inheritdoc/>
    public override string? Corpus => replay.Corpus;

    /// <inheritdoc/>
    protected override void Configure(IMachineBuilder<TState, TTrigger> machine) =>
        replay.Configure(machine);
}

/// <summary>A user-owned machine from its IR, served to clients as a <see cref="TraxMachineAttribute"/> machine is.</summary>
[TraxMachine]
internal sealed class ServedIrMachine<TState, TTrigger>(IrReplay replay)
    : IrServedMachine<TState, TTrigger>(replay)
    where TState : struct, Enum
    where TTrigger : struct, Enum;

/// <summary>A system-owned machine from its IR: only the server creates and moves its instances.</summary>
internal sealed class SystemIrMachine<TState, TTrigger>(IrReplay replay)
    : IrServedMachine<TState, TTrigger>(replay)
    where TState : struct, Enum
    where TTrigger : struct, Enum;

/// <summary>
/// Everything one machine's <see cref="IrServedMachine{TState,TTrigger}.Configure"/> replays: the IR, its entry in the
/// host file, the types emitted for it and the trains its invoking states run. The IR is read here with the engine's own
/// readers (<see cref="IrMachine.ReadSchema"/>, <see cref="IrMachine.ReadRule"/>, <see cref="IrMachine.ReadReduction"/>),
/// so it is parsed one way.
/// </summary>
internal sealed class IrReplay(
    JsonObject ir,
    HostMachine host,
    IrTypes types,
    IReadOnlyDictionary<string, Type> trains,
    string? corpus
)
{
    private static readonly JsonSerializerOptions TrainInputJson = new(JsonSerializerDefaults.Web);

    internal string? Corpus => corpus;

    internal void Configure<TState, TTrigger>(IMachineBuilder<TState, TTrigger> m)
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        var initialContext = (JsonObject)ir["initialContext"]!.DeepClone();
        m.Id(ir["id"]!.GetValue<string>())
            .Version(ir["version"]!.GetValue<int>())
            .StartsAt(
                State<TState>(ir["initialState"]),
                () => (JsonObject)initialContext.DeepClone()
            );
        if (host.SystemKey is not null)
            m.SystemOwned();

        foreach (var (state, schema) in ir["context"]!.AsObject())
            ((IIrStateBuilder<TState, TTrigger>)m.In(State<TState>(state))).Context(
                types.Contexts[state],
                IrMachine.ReadSchema(schema!.AsObject())
            );
        foreach (var state in ir["committedStates"]!.AsArray())
            m.In(State<TState>(state)).Committed();
        foreach (var (state, rule) in ir["invariants"] as JsonObject ?? [])
            m.In(State<TState>(state)).Requires(IrMachine.ReadRule(rule!.AsObject()));

        var inputs = ir["inputs"]!.AsObject();
        foreach (var node in ir["transitions"]!.AsArray())
        {
            var edge = node!.AsObject();
            var trigger = edge["trigger"]!.GetValue<string>();
            var t = m.In(State<TState>(edge["from"])).On(Enum.Parse<TTrigger>(trigger));
            if (inputs[trigger] is JsonObject schema)
                ((IIrTransitionBuilder<TState, TTrigger>)t).WithInput(
                    types.Inputs[trigger],
                    IrMachine.ReadSchema(schema)
                );
            if (edge["guard"] is JsonObject guard)
                t.When(IrMachine.ReadRule(guard));
            if (edge["guardMessage"] is { } message)
                t.Because(message.GetValue<string>());
            if (edge["reduce"] is JsonObject reduce)
                t.Reduce(IrMachine.ReadReduction(reduce));
            if (host.Triggers.TryGetValue(trigger, out var access) && access.Client is { } rule)
                Apply(t, rule);
            t.To(State<TState>(edge["to"]));
        }

        ConfigureOutcomes(m);
        ConfigureTimers(m);
        ConfigureAccess(m);
        if (ir["differential"] is JsonObject differential)
            m.Differential(d => ConfigureDifferential(d, differential));
    }

    // Each invoking state runs the train the host file names for it, given the context fields it maps, and routes each
    // outcome's edges in the IR's order, which is the order they are tried in.
    private void ConfigureOutcomes<TState, TTrigger>(IMachineBuilder<TState, TTrigger> m)
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        var byState = (ir["outcomes"] as JsonObject ?? [])
            .Select(o => o.Value!.AsObject())
            .GroupBy(o => o["state"]!.GetValue<string>(), StringComparer.Ordinal);
        foreach (var outcomes in byState)
        {
            var invoke = host.Invokes[outcomes.Key];
            var train = trains[outcomes.Key];
            var (input, output) = IrMachines.TrainTypes(train);
            var mapper = typeof(IrReplay)
                .GetMethod(nameof(InputOf), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(input)
                .Invoke(null, [invoke.Input])!;
            var invokes = typeof(IStateBuilder<TState, TTrigger>)
                .GetMethod(nameof(IStateBuilder<TState, TTrigger>.Invokes))!
                .MakeGenericMethod(train, input, output);
            var builder =
                (IInvokeBuilder<TState, TTrigger>)
                    invokes.Invoke(m.In(State<TState>(outcomes.Key)), [mapper])!;

            foreach (var outcome in outcomes)
            foreach (var node in outcome["edges"]!.AsArray())
            {
                var edge = node!.AsObject();
                var to = State<TState>(edge["to"]);
                var reduce = edge["reduce"] is JsonObject r ? IrMachine.ReadReduction(r) : null;
                switch (outcome["outcome"]!.GetValue<string>())
                {
                    case "done":
                        builder.OnDone(
                            to,
                            edge["guard"] is JsonObject g ? IrMachine.ReadRule(g) : null,
                            reduce
                        );
                        break;
                    case "failed":
                        builder.OnFailed(to, reduce);
                        break;
                    default:
                        builder.OnCancelled(to, reduce);
                        break;
                }
            }
        }
    }

    // The train's input, from the context fields the host file maps onto its fields.
    private static Func<JsonObject, TInput> InputOf<TInput>(
        IReadOnlyDictionary<string, string> mapping
    ) =>
        context =>
        {
            var input = new JsonObject();
            foreach (var (field, from) in mapping)
                input[field] = context[from]?.DeepClone();
            return input.Deserialize<TInput>(TrainInputJson)!;
        };

    private void ConfigureTimers<TState, TTrigger>(IMachineBuilder<TState, TTrigger> m)
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        foreach (var node in ir["timers"] as JsonArray ?? [])
        {
            var timer = node!.AsObject();
            var state = timer["state"]!.GetValue<string>();
            var target = State<TState>(timer["target"]);
            if (timer["after"] is { } after)
            {
                m.In(State<TState>(state)).After(Duration(after)).GoTo(target);
                continue;
            }
            // A duration read from the context: the field's property on the state's emitted context record.
            var field = timer["field"]!.AsObject();
            var context = types.Contexts[state];
            var parameter = Expression.Parameter(context, "c");
            var property = context.GetProperty(
                IrTypes.PropertyName(field["name"]!.GetValue<string>(), $"{state}Context")
            )!;
            var selector = Expression.Lambda(
                typeof(Func<,>).MakeGenericType(context, typeof(TimeSpan?)),
                Expression.Property(parameter, property),
                parameter
            );
            var afterField = typeof(IStateBuilder<TState, TTrigger>)
                .GetMethods()
                .Single(mi => mi.Name == "After" && mi.IsGenericMethodDefinition)
                .MakeGenericMethod(context);
            var builder =
                (ITimerBuilder<TState, TTrigger>)
                    afterField.Invoke(
                        m.In(State<TState>(state)),
                        [selector, Duration(field["min"]), Duration(field["max"])]
                    )!;
            builder.GoTo(target);
        }
    }

    // Who may start, read and fire the machine, from its entry in the host file. A user's machine is read and discarded
    // by its owner, narrowed by the read rule's roles (MS-18 decision 15).
    private void ConfigureAccess<TState, TTrigger>(IMachineBuilder<TState, TTrigger> m)
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        if (host.SystemKey is not null)
            return;
        if (host.Start is { } start)
            typeof(IrReplay)
                .GetMethod(nameof(StartWith), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(typeof(TState), typeof(TTrigger), types.Start)
                .Invoke(null, [m, start]);
        if (host.Read is { } read)
        {
            m.Get(r => OwnerOnly(r, read));
            m.Watch(r => OwnerOnly(r, read));
            m.Discard(r => OwnerOnly(r, read));
        }
    }

    private static void StartWith<TState, TTrigger, TStart>(
        IMachineBuilder<TState, TTrigger> m,
        AccessRule rule
    )
        where TState : struct, Enum
        where TTrigger : struct, Enum =>
        m.Start<TStart>(s =>
        {
            if (rule.IsAnyAuthenticated)
                s.AnyAuthenticated();
            else
                foreach (var set in rule.RoleSets)
                    s.Requires([.. set]);
        });

    private static void OwnerOnly(IReadRuleBuilder r, AccessRule read)
    {
        r.OwnerOnly();
        foreach (var set in read.RoleSets)
            r.Requires([.. set]);
    }

    private static void Apply<TState, TTrigger>(
        ITransitionBuilder<TState, TTrigger> t,
        AccessRule rule
    )
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        if (rule.IsAnyAuthenticated)
            t.AnyAuthenticated();
        else
            foreach (var set in rule.RoleSets)
                t.Requires([.. set]);
    }

    private static void ConfigureDifferential<TState, TTrigger>(
        IDifferentialBuilder<TState, TTrigger> d,
        JsonObject differential
    )
        where TState : struct, Enum
        where TTrigger : struct, Enum
    {
        foreach (var (name, samples) in differential["samples"] as JsonObject ?? [])
        foreach (var sample in samples!.AsArray())
            if (OutcomeTriggers.TryParse(name, out var state, out _))
                d.OutcomeSample(Enum.Parse<TState>(state), sample!.DeepClone());
            else
                d.Sample(Enum.Parse<TTrigger>(name), sample!.DeepClone());
        foreach (var (state, seeds) in differential["seeds"] as JsonObject ?? [])
        // One seed is written as the context itself, several as a list of them.
        foreach (var seed in seeds is JsonArray several ? several.ToArray() : [seeds])
            d.Seed(Enum.Parse<TState>(state), seed!.DeepClone().AsObject());
        foreach (var context in differential["contexts"] as JsonArray ?? [])
            d.Probe(context!.DeepClone().AsObject());
    }

    private static TState State<TState>(JsonNode? name)
        where TState : struct, Enum => Enum.Parse<TState>(name!.GetValue<string>());

    private static TState State<TState>(string name)
        where TState : struct, Enum => Enum.Parse<TState>(name);

    private static TimeSpan Duration(JsonNode? node) =>
        IsoDuration.TryParse(node!.GetValue<string>(), out var duration)
            ? duration
            : throw new InvalidOperationException($"'{node}' is not an ISO-8601 duration.");
}
