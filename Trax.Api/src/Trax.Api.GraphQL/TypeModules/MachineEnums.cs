using System.Collections;
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
/// only when the wire spells an enum the way the store does. HotChocolate binds one GraphQL type to a CLR enum, so a
/// train that shares the enum would see the same spelling: that is refused at startup unless the host accepts it with
/// <c>ShareEnumWithMachines</c> (<see cref="SharingProblems"/>). See
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
        if (IsCollection(type))
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

    private static bool IsCollection(Type type) =>
        type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type);

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
