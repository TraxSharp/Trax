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
/// train that shares the enum sees the same spelling. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
internal static partial class MachineEnums
{
    /// <summary>One enum type per CLR enum the <paramref name="machines"/> reach, in a stable order.</summary>
    public static IEnumerable<ITypeSystemMember> Types(IReadOnlyList<IMachine> machines)
    {
        var enums = new SortedDictionary<string, Type>(StringComparer.Ordinal);
        var seen = new HashSet<Type>();
        foreach (var machine in machines)
        {
            foreach (var field in MachineContextFields.Of(machine, machine.States))
                Walk(field.ClrType, enums, seen);
            if (machine.StartInputType is { } start)
                Walk(start, enums, seen);
            foreach (var trigger in MachineSurface.ClientTriggers(machine))
                if (trigger.InputType is { } input)
                    Walk(input, enums, seen);
        }
        return enums.Values.Select(Create);
    }

    private static void Walk(Type type, SortedDictionary<string, Type> enums, HashSet<Type> seen)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (!seen.Add(type))
            return;
        if (type.IsEnum)
        {
            if (!type.IsDefined(typeof(FlagsAttribute), inherit: false))
                enums[type.FullName ?? type.Name] = type;
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
        if (
            type.IsPrimitive
            || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true
        )
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
        where TEnum : struct, Enum =>
        new(d =>
        {
            d.BindValuesExplicitly();
            foreach (var value in Enum.GetValues<TEnum>())
            {
                var name = JsonSerializer.Deserialize<string>(
                    JsonSerializer.Serialize(value, MachineSurface.Json)
                )!;
                if (!GraphQLName().IsMatch(name))
                    throw new InvalidOperationException(
                        $"The enum {typeof(TEnum).FullName} is held by a served machine, so GraphQL spells its "
                            + $"values as the server stores them, but '{name}' is not a GraphQL name. Rename the member."
                    );
                d.Value(value).Name(name);
            }
        });

    [GeneratedRegex("^[_A-Za-z][_0-9A-Za-z]*$")]
    private static partial Regex GraphQLName();
}
