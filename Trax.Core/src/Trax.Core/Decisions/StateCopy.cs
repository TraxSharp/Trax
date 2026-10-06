using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Trax.Core.Utils;

namespace Trax.Core.Decisions;

/// <summary>
/// The copy of a decision's state each shadow is handed: written to JSON once per asking and read
/// back separately for each shadow, so no shadow shares an object with the run or with another
/// shadow.
/// </summary>
/// <remarks>
/// It is written with the options the System One adapter writes a state with
/// (<see cref="JsonSerializerDefaults.Web"/>), so a shadow that sends the state on sees what it
/// would have seen from the original. A property with no setter that holds a collection or an
/// object is filled in from the JSON rather than left as its constructor made it. A value tuple is
/// the one exception: those options write no fields, and a tuple holds nothing else, so its items
/// are written and read as its members (<c>item1</c>, <c>item2</c>, and so on) and a shadow asked
/// about a tuple sees what the live decider sees.
/// </remarks>
internal static class StateCopy
{
    internal static readonly JsonSerializerOptions Options = MakeOptions();

    private static readonly ConcurrentDictionary<Type, string?> Problems = new();

    /// <summary>The state written once, to be read back for each shadow.</summary>
    public static byte[] Snapshot(object state) =>
        JsonSerializer.SerializeToUtf8Bytes(state, state.GetType(), Options);

    /// <summary>A copy of the state, read back from <paramref name="snapshot"/> as <paramref name="type"/>.</summary>
    public static object Read(byte[] snapshot, Type type) =>
        JsonSerializer.Deserialize(snapshot, type, Options)
        ?? throw new JsonException("it was read back as null");

    /// <summary>
    /// Why a state declared as <paramref name="declared"/> cannot be copied through JSON, when the
    /// type alone says so, or null. An interface or abstract class is not judged: the copy is made
    /// of the state's own type, which only the run knows.
    /// </summary>
    public static string? ProblemWith(Type declared) =>
        declared.IsInterface || declared.IsAbstract
            ? null
            : Problems.GetOrAdd(declared, type => Walk(type, "", [], readBack: true));

    private static JsonSerializerOptions MakeOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { WriteTupleItems } },
        };

        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>Writes and reads a value tuple's items, which are fields, as its members.</summary>
    private static void WriteTupleItems(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object || !IsValueTuple(info.Type))
            return;

        foreach (var field in info.Type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var item = info.CreateJsonPropertyInfo(
                field.FieldType,
                info.Options.PropertyNamingPolicy?.ConvertName(field.Name) ?? field.Name
            );
            item.Get = field.GetValue;
            item.Set = field.SetValue;
            item.AttributeProvider = field;
            info.Properties.Add(item);
        }
    }

    private static bool IsValueTuple(Type type) =>
        type.IsValueType
        && type.IsGenericType
        && type.Namespace == "System"
        && type.Name.StartsWith("ValueTuple`", StringComparison.Ordinal);

    /// <summary>
    /// Looks through <paramref name="type"/> and what it holds for something JSON cannot write, or,
    /// where <paramref name="readBack"/> says the value is read back, cannot read back.
    /// </summary>
    private static string? Walk(
        Type type,
        string path,
        System.Collections.Generic.HashSet<(Type, bool)> seen,
        bool readBack
    )
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (CannotBeWritten(underlying))
            return $"{Where(path)} is a '{underlying.ReadableName()}', which JSON cannot write";

        if (!seen.Add((underlying, readBack)))
            return null;

        JsonTypeInfo info;

        try
        {
            info = Options.GetTypeInfo(underlying);
        }
        catch (Exception e)
            when (e is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return $"{Where(path)} cannot be written as JSON: {e.Message}";
        }

        // A type that declares its subtypes is read back as whichever one was written.
        if (info.PolymorphismOptions is not null)
            return null;

        switch (info.Kind)
        {
            case JsonTypeInfoKind.Enumerable:
            case JsonTypeInfoKind.Dictionary:
                return info.ElementType is { } element
                    ? Walk(element, $"{path}[]", seen, readBack)
                    : null;

            case JsonTypeInfoKind.Object:
                return ObjectProblem(underlying, info, path, seen, readBack);

            default:
                return null;
        }
    }

    private static string? ObjectProblem(
        Type type,
        JsonTypeInfo info,
        string path,
        System.Collections.Generic.HashSet<(Type, bool)> seen,
        bool readBack
    )
    {
        var constructor = info.ConstructorAttributeProvider as ConstructorInfo;
        var parameters = constructor?.GetParameters() ?? [];

        if (readBack)
        {
            if (type.IsInterface || type.IsAbstract)
                return $"{Where(path)} is declared as '{type.ReadableName()}', "
                    + $"{(type.IsInterface ? "an interface" : "an abstract class")}, which JSON "
                    + "cannot read back without knowing which type to make";

            if (info.CreateObject is null && constructor is null)
                return $"{Where(path)} is a '{type.ReadableName()}', which has no constructor "
                    + "JSON can read it back through";

            foreach (var parameter in parameters)
                if (
                    !info.Properties.Any(p =>
                        string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase)
                    )
                )
                    return $"{Where(path)} is a '{type.ReadableName()}', whose constructor takes "
                        + $"'{parameter.Name}', which is none of the properties JSON writes, so "
                        + "it cannot be read back";
        }

        foreach (var property in info.Properties)
        {
            // A property with no setter is written but, unless it is filled in place, not read.
            var read =
                readBack
                && (
                    property.Set is not null
                    || parameters.Any(p =>
                        string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase)
                    )
                );

            var member = (property.AttributeProvider as MemberInfo)?.Name ?? property.Name;

            if (
                Walk(
                    property.PropertyType,
                    path.Length == 0 ? member : $"{path}.{member}",
                    seen,
                    read
                ) is
                { } problem
            )
                return problem;
        }

        return null;
    }

    /// <summary>The types System.Text.Json refuses to write whatever the options.</summary>
    private static bool CannotBeWritten(Type type) =>
        typeof(Delegate).IsAssignableFrom(type)
        || typeof(MemberInfo).IsAssignableFrom(type)
        || type == typeof(IntPtr)
        || type == typeof(UIntPtr)
        || type.IsPointer
        || type.IsByRef;

    private static string Where(string path) =>
        path.Length == 0 ? "the state" : $"its member '{path}'";
}
