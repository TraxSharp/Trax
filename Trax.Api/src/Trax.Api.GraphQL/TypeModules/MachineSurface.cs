using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// What the <c>machines</c> root serves for each exposed machine, and the names it serves them under: one field per
/// machine, named from its kebab-case id in camelCase (<c>topic-map</c> becomes <c>topicMap</c>); under it
/// <c>start</c>, <c>discard</c> and one field per client event, named from the trigger as the server's camelCase
/// serializer writes it (<c>CancelBuild</c> becomes <c>cancelBuild</c>), and <c>get</c> on the query side. The types
/// it generates carry the machine's name and the word <c>Machine</c> (<c>TopicMapMachineState</c>), so a CLR type the
/// schema already has, such as a machine's own state enum a train returns, keeps its name.
/// See <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
internal static class MachineSurface
{
    /// <summary>The field every exposed machine hangs off, on both roots.</summary>
    public const string RootField = "machines";

    /// <summary>The namespace type <c>Mutation.machines</c> resolves to.</summary>
    public const string MutationsType = "MachinesMutations";

    /// <summary>The namespace type <c>Query.machines</c> resolves to.</summary>
    public const string QueriesType = "MachinesQueries";

    /// <summary>The type <c>discard</c> answers with, shared by every machine.</summary>
    public const string DiscardResultType = "MachineDiscardResult";

    /// <summary>The operations every machine has, which no event may be named like.</summary>
    public static readonly IReadOnlySet<string> Operations = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "start",
        "discard",
        "get",
    };

    /// <summary>
    /// How a machine's input and context records cross the wire: camelCase, a <c>[JsonPropertyName]</c> honoured,
    /// enums as their names, a <see cref="TimeSpan"/> as the ISO-8601 duration a timer reads (<c>"PT5M"</c>), a
    /// null field left out, so the server applies an input exactly as a client that omits an empty field sends it,
    /// and a computed property (one reading never sets) left out, as the input's schema leaves it out.
    /// </summary>
    public static readonly JsonSerializerOptions Json = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { SchemaReflection.LeaveOutComputedProperties },
            },
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new IsoDurationJsonConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>The machine's field under <c>machines</c>: its id in camelCase (<c>topic-map</c> is <c>topicMap</c>).</summary>
    public static string FieldName(string machineId)
    {
        var pascal = PascalName(machineId);
        return pascal.Length == 0 ? pascal : char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    /// <summary>The machine's id in PascalCase (<c>topic-map</c> is <c>TopicMap</c>), the stem of its generated types.</summary>
    public static string PascalName(string machineId) =>
        string.Concat(
            machineId
                .Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
        );

    /// <summary>An event's field: the trigger as the server's camelCase serializer writes it.</summary>
    public static string EventField(string trigger) =>
        JsonNamingPolicy.CamelCase.ConvertName(trigger);

    /// <summary>The JSON name of a record's property: its <c>[JsonPropertyName]</c>, else its name in camelCase.</summary>
    public static string JsonName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);

    /// <summary>The type <c>machines.&lt;machine&gt;</c> resolves to on the mutation root.</summary>
    public static string MutationTypeName(string pascal) => $"{pascal}MachineMutations";

    /// <summary>The type <c>machines.&lt;machine&gt;</c> resolves to on the query root.</summary>
    public static string QueryTypeName(string pascal) => $"{pascal}MachineQueries";

    /// <summary>The type every operation of the machine answers with.</summary>
    public static string ResultTypeName(string pascal) => $"{pascal}MachineResult";

    /// <summary>The enum of the machine's states.</summary>
    public static string StateTypeName(string pascal) => $"{pascal}MachineState";

    /// <summary>The object of the machine's context fields.</summary>
    public static string ContextTypeName(string pascal) => $"{pascal}MachineContext";

    /// <summary>
    /// The machines served under the root: each registered machine marked <c>[TraxMachine]</c>, however it was
    /// registered, ordered by type name, each once. The attribute is read off the machine's own type, so a subclass of a
    /// marked machine is not served by its base being so. Nothing is served on a host without
    /// <c>AddStateMachines</c> (<paramref name="options"/> null).
    /// </summary>
    public static IReadOnlyList<IMachine> Exposed(
        IEnumerable<IMachine> machines,
        StateMachineOptions? options
    ) => options is null ? [] : StateMachineOptions.Served(machines);

    /// <summary>
    /// Every reason the exposed machines cannot be served under the names the root gives them, one sentence each:
    /// two machines whose ids give one field, and an event named like an operation every machine has.
    /// </summary>
    public static IReadOnlyList<string> NameProblems(IReadOnlyList<IMachine> machines)
    {
        var problems = new List<string>();

        foreach (
            var clash in machines
                .GroupBy(m => FieldName(m.Name), StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
        )
            problems.Add(
                "The machines "
                    + string.Join(" and ", clash.Select(m => $"'{m.Name}'"))
                    + $" are all served under the field 'machines {{ {clash.Key} }}'. Give one of them another id."
            );

        foreach (var machine in machines)
        foreach (var trigger in ClientTriggers(machine))
            if (Operations.Contains(EventField(trigger.Name)))
                problems.Add(
                    $"The machine '{machine.Name}' declares the event {trigger.Name}, whose field "
                        + $"'{EventField(trigger.Name)}' is an operation every machine has (start, discard, get). "
                        + "Rename the trigger."
                );

        foreach (var machine in machines)
        foreach (
            var clash in ClientTriggers(machine)
                .GroupBy(t => EventField(t.Name), StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
        )
            problems.Add(
                $"The machine '{machine.Name}' declares the events "
                    + string.Join(" and ", clash.Select(t => t.Name))
                    + $", which are both served as the field '{clash.Key}'. Rename one of them."
            );

        return problems;
    }

    /// <summary>The triggers a client sends: the client ones and the effect-bound ones, never an outcome or a timer.</summary>
    public static IEnumerable<MachineTrigger> ClientTriggers(IMachine machine) =>
        machine.Triggers.Where(t => t.Kind is TriggerKind.Client or TriggerKind.EffectBound);

    /// <summary>
    /// Whether a record has a property GraphQL can carry. A record with none is no input object at all, so its
    /// operation takes no <c>input</c> argument.
    /// </summary>
    public static bool HasFields(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Any(p => p.PropertyType != typeof(object) && p.GetIndexParameters().Length == 0);
}
