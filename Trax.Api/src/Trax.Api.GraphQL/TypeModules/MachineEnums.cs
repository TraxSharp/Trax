using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using HotChocolate.Types;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// The GraphQL enums of the CLR enums a machine's context, start input or event inputs hold, each value named as the
/// server stores it (<see cref="MachineSurface.Json"/>, the C# member name) rather than HotChocolate's upper snake
/// case. The twin applies an input as the client sends it and the server stores what it serializes, so the two agree
/// only when the wire spells an enum the way the store does. HotChocolate binds one GraphQL type to a CLR enum, so any
/// other field that carries the enum would see the same spelling: that is refused at startup unless the host accepts
/// it with <c>ShareEnumWithMachines</c> (<see cref="SharingProblems"/> for trains and query models,
/// <see cref="MachineEnumSharingInterceptor"/> for every field of the built schema). An enum of Trax's own is refused
/// whatever the host accepted (<see cref="TraxEnumProblems"/>). See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
internal static partial class MachineEnums
{
    /// <summary>One enum type per CLR enum the <paramref name="machines"/> reach, in a stable order.</summary>
    public static IEnumerable<ITypeSystemMember> Types(IReadOnlyList<IMachine> machines) =>
        machines
            .SelectMany(Reached)
            .DistinctBy(t => t.FullName ?? t.Name)
            .OrderBy(t => t.FullName ?? t.Name, StringComparer.Ordinal)
            .Select(Create);

    /// <summary>The CLR enums <paramref name="machine"/>'s context, start input and event inputs hold.</summary>
    public static IReadOnlyCollection<Type> Reached(IMachine machine)
    {
        var enums = new HashSet<Type>();
        var seen = new HashSet<Type>();
        foreach (var field in MachineContextFields.Of(machine, machine.States))
            Walk(field.ClrType, enums, seen);
        if (machine.StartInputType is { } start)
            Walk(start, enums, seen);
        foreach (var trigger in MachineSurface.ClientTriggers(machine))
            if (trigger.InputType is { } input)
                Walk(input, enums, seen);
        return enums;
    }

    /// <summary>The CLR enums <paramref name="type"/> holds, through its properties and collections.</summary>
    public static IReadOnlyCollection<Type> Reached(Type type)
    {
        var enums = new HashSet<Type>();
        Walk(type, enums, []);
        return enums;
    }

    /// <summary>
    /// Every enum a machine respells that another surface of the schema also reaches, one sentence each. HotChocolate
    /// binds one GraphQL type to a CLR enum, so the other surface's wire changes from <c>UPPER_SNAKE</c> to the member
    /// names the moment a machine holds the enum, and a document that sent the old literal stops validating. Refused
    /// unless the host accepted the respelling for that enum.
    /// </summary>
    public static IReadOnlyList<string> SharingProblems(
        IReadOnlyList<IMachine> machines,
        MachineEnumNeighbours? neighbours
    )
    {
        if (neighbours is null || neighbours.Surfaces.Count == 0)
            return [];

        var held = new Dictionary<Type, List<string>>();
        foreach (var machine in machines)
        foreach (var type in Reached(machine))
        {
            if (!held.TryGetValue(type, out var names))
                held[type] = names = [];
            names.Add(machine.Name);
        }

        var problems = new List<string>();
        foreach (var (surface, root) in neighbours.Surfaces)
        foreach (
            var type in Reached(root)
                .Where(held.ContainsKey)
                .Where(t => !neighbours.Accepted.Contains(t))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
        )
            problems.Add(
                $"The enum {type.FullName} is held by the machine "
                    + string.Join(" and ", held[type].Select(m => $"'{m}'"))
                    + $" and reached by {surface}, whose wire would change from upper snake case to the member "
                    + "names: GraphQL has one type per enum, and a machine's spells its values as the server stores "
                    + $"them. Give {surface} its own enum, or call ShareEnumWithMachines<{type.Name}>() on "
                    + "AddTraxGraphQL to accept the new spelling."
            );
        // A train that takes the enum in and gives it back is one problem, not two.
        return problems.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Every enum declared in a Trax assembly that a served machine holds, one sentence each. Trax's own fields carry
    /// its enums (<c>TrainLifecycleEvent.trainState</c>, <c>operations { executions(trainState:) }</c>), and the
    /// dashboard and every generated client send them in upper snake case, so a machine never shares one, whatever the
    /// host accepted with <c>ShareEnumWithMachines</c>.
    /// </summary>
    public static IReadOnlyList<string> TraxEnumProblems(IReadOnlyList<IMachine> machines)
    {
        var held = new SortedDictionary<string, (Type Type, List<string> Machines)>(
            StringComparer.Ordinal
        );
        foreach (var machine in machines)
        foreach (var type in Reached(machine).Where(IsTrax))
        {
            var key = type.FullName ?? type.Name;
            if (!held.TryGetValue(key, out var entry))
                held[key] = entry = (type, []);
            entry.Machines.Add(machine.Name);
        }
        return held
            .Values.Select(e =>
                $"The enum {e.Type.FullName} is Trax's own and is held by the machine "
                + string.Join(" and ", e.Machines.Select(m => $"'{m}'"))
                + ". "
                + TraxEnumRemedy(e.Type)
            )
            .ToList();
    }

    /// <summary>
    /// Why a machine cannot share <paramref name="type"/>, a Trax enum, and what to do instead. It names the assembly,
    /// since Trax tells its own by the assembly's name: a consumer assembly named <c>Trax.Billing</c> counts too.
    /// </summary>
    public static string TraxEnumRemedy(Type type) =>
        $"It is declared in {type.Assembly.GetName().Name}, and an assembly named Trax or Trax.* counts as Trax's "
        + "own, other than Trax.Samples.* and a test project (a Tests segment). "
        + "A machine spells its enums as the server stores them, GraphQL has one type per enum, and Trax's own fields "
        + $"carry {type.Name} in upper snake case for the dashboard and every client, so Trax never shares its enums "
        + "with machines. Give the machine its own enum, in an assembly not named that way, and map it to and from "
        + type.Name
        + " where the machine needs it.";

    /// <summary>
    /// Whether <paramref name="type"/> is declared in one of Trax's own assemblies: one named <c>Trax</c> or
    /// <c>Trax.*</c>, other than the samples (<c>Trax.Samples.*</c>) and the test projects (a <c>Tests</c> segment),
    /// which are consumer code. The name is all there is to read, since Trax's assemblies are not strong-named, so a
    /// consumer assembly named <c>Trax.Billing</c> counts as Trax's own too, and its enums are never shared.
    /// </summary>
    public static bool IsTrax(Type type)
    {
        if (type.Assembly.GetName().Name is not { } name)
            return false;
        if (name != "Trax" && !name.StartsWith("Trax.", StringComparison.Ordinal))
            return false;
        var segments = name.Split('.');
        return segments[1..] is not ["Samples", ..] && !segments.Contains("Tests");
    }

    /// <summary>
    /// Whether <paramref name="type"/> is the framework's: in <c>System</c> or a namespace under it, and not merely one
    /// whose name starts with the word.
    /// </summary>
    public static bool IsFramework(Type type) =>
        type.Namespace is { } ns
        && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal));

    private static void Walk(Type type, HashSet<Type> enums, HashSet<Type> seen)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (!seen.Add(type))
            return;
        if (type.IsEnum)
        {
            if (!type.IsDefined(typeof(FlagsAttribute), inherit: false))
                enums.Add(type);
            return;
        }
        if (type.IsArray)
        {
            Walk(type.GetElementType()!, enums, seen);
            return;
        }
        // A collection is its elements, however it is declared: List<T>, a class deriving from it, a dictionary's
        // KeyValuePair<K, V>.
        if (type != typeof(string) && Elements(type) is { Count: > 0 } elements)
        {
            foreach (var element in elements)
                Walk(element, enums, seen);
            return;
        }
        // A framework generic (KeyValuePair, Tuple, ValueTuple) holds what its arguments hold.
        if (type.IsGenericType && IsFramework(type))
        {
            foreach (var argument in type.GetGenericArguments())
                Walk(argument, enums, seen);
            return;
        }
        // A framework type (a string, a date, a JsonObject) holds no enum of the consumer's.
        if (type.IsPrimitive || IsFramework(type))
            return;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.GetIndexParameters().Length == 0)
                Walk(property.PropertyType, enums, seen);
    }

    // The T of each IEnumerable<T> the type is or implements.
    private static List<Type> Elements(Type type) =>
        (type.IsInterface ? type.GetInterfaces().Append(type) : type.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(i => i.GetGenericArguments()[0])
            .ToList();

    private static ITypeSystemMember Create(Type enumType) =>
        (ITypeSystemMember)
            typeof(MachineEnums)
                .GetMethod(nameof(Stored), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(enumType)
                .Invoke(null, null)!;

    private static EnumType<TEnum> Stored<TEnum>()
        where TEnum : struct, Enum => new MachineEnumType<TEnum>();

    /// <summary>
    /// Why <paramref name="name"/>, a stored spelling of a member of <paramref name="enumType"/>, cannot be a GraphQL
    /// enum value, or null when it can.
    /// </summary>
    public static string? ValueNameProblem(Type enumType, string name)
    {
        var why =
            !GraphQLName().IsMatch(name) ? "is not a GraphQL name"
            : name.StartsWith("__", StringComparison.Ordinal)
                ? "starts with two underscores, which GraphQL keeps for introspection"
            : name is "true" or "false" or "null"
                ? "is a GraphQL literal, which no enum value may be"
            : null;
        return why is null
            ? null
            : $"The enum {enumType.FullName} is held by a served machine, so GraphQL spells its values as the "
                + $"server stores them, but '{name}' {why}. Rename the member.";
    }

    [GeneratedRegex("^[_A-Za-z][_0-9A-Za-z]*$")]
    private static partial Regex GraphQLName();
}

/// <summary>
/// A machine enum's GraphQL type: its values named as the server stores them. Its own class so a type the host binds
/// to the same CLR enum can be told apart from it (<see cref="MachineEnumBindingInterceptor"/>).
/// </summary>
internal sealed class MachineEnumType<TEnum>() : EnumType<TEnum>(Describe)
    where TEnum : struct, Enum
{
    private static void Describe(IEnumTypeDescriptor<TEnum> d)
    {
        d.BindValuesExplicitly();
        foreach (var value in Enum.GetValues<TEnum>())
        {
            var name = JsonSerializer.Deserialize<string>(
                JsonSerializer.Serialize(value, MachineSurface.Json)
            )!;
            if (MachineEnums.ValueNameProblem(typeof(TEnum), name) is { } problem)
                throw new InvalidOperationException(problem);
            d.Value(value).Name(name);
        }
    }
}

/// <summary>
/// The other surfaces of the schema a machine enum could also reach (each exposed train's input and output, each query
/// model's entity), and the enums the host accepted the machine's spelling for.
/// </summary>
/// <param name="Surfaces">How a refusal names the surface, and the CLR type it starts from.</param>
/// <param name="Accepted">The enums <c>ShareEnumWithMachines</c> named.</param>
internal sealed record MachineEnumNeighbours(
    IReadOnlyList<(string Surface, Type Root)> Surfaces,
    IReadOnlySet<Type> Accepted
);
