using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

/// <summary>
/// Whether a checkpoint's state type survives being stored as JSON and read back as the same
/// value. One that does not would come back different, and a decision asked after a resume would
/// hash a different state, so its recorded answer silently stops replaying.
/// </summary>
/// <remarks>
/// The rules follow System.Text.Json's defaults, which the store writes with: public properties
/// are written, fields are not, and a value is read back through a setter, an init accessor or a
/// constructor parameter of the same name. Every class reached must be sealed, because a derived
/// instance would be read back as its declared type, and no member may be typed <c>object</c> or
/// an interface other than a framework collection, because nothing says what to read it back as.
/// </remarks>
internal static class CheckpointState
{
    private static readonly System.Collections.Generic.HashSet<Type> Leaves =
    [
        typeof(string),
        typeof(decimal),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(DateOnly),
        typeof(TimeOnly),
        typeof(TimeSpan),
        typeof(Guid),
        typeof(Uri),
    ];

    /// <summary>
    /// A fingerprint of how <paramref name="state"/> is written as JSON: every member's JSON name
    /// and type, recursively, as 64 lowercase hex digits. A stored state whose fingerprint differs
    /// from the type's now would be read with members quietly defaulted, so a resume refuses it.
    /// </summary>
    public static string Fingerprint(Type state)
    {
        var shape = new StringBuilder();
        Describe(state, shape, []);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(shape.ToString())));
    }

    private static void Describe(
        Type type,
        StringBuilder shape,
        System.Collections.Generic.HashSet<Type> seen
    )
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            shape.Append('?');
            type = underlying;
        }

        shape.Append(type.FullName ?? type.Name);

        if (type.IsEnum)
        {
            shape.Append('{').AppendJoin(',', Enum.GetNames(type)).Append('}');
            return;
        }

        if (type.IsPrimitive || Leaves.Contains(type) || !seen.Add(type))
            return;

        if (type.IsArray)
        {
            shape.Append('[');
            Describe(type.GetElementType()!, shape, seen);
            shape.Append(']');
            return;
        }

        if (IsFrameworkCollection(type))
        {
            shape.Append('<');
            foreach (var argument in type.GetGenericArguments())
            {
                Describe(argument, shape, seen);
                shape.Append(',');
            }
            shape.Append('>');
            return;
        }

        shape.Append('{');
        foreach (
            var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
                .OrderBy(p => JsonName(p), StringComparer.Ordinal)
        )
        {
            shape.Append(JsonName(property)).Append(':');
            Describe(property.PropertyType, shape, seen);
            shape.Append(';');
        }
        shape.Append('}');
    }

    private static string JsonName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;

    /// <summary>Everything about <paramref name="state"/> that would not round-trip, or nothing.</summary>
    public static IReadOnlyList<string> Problems(Type state)
    {
        var problems = new List<string>();
        Visit(state, state.ReadableName(), problems, []);
        return problems;
    }

    private static void Visit(
        Type type,
        string at,
        List<string> problems,
        System.Collections.Generic.HashSet<Type> seen
    )
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            type = underlying;

        if (type.IsPrimitive || type.IsEnum || Leaves.Contains(type) || !seen.Add(type))
            return;

        if (type == typeof(object))
        {
            problems.Add(
                $"'{at}' is typed object, so nothing says what to read it back as. Give it a "
                    + "concrete type."
            );
            return;
        }

        if (type.IsArray)
        {
            Visit(type.GetElementType()!, $"{at}[]", problems, seen);
            return;
        }

        if (IsFrameworkCollection(type))
        {
            foreach (var argument in type.GetGenericArguments())
                Visit(argument, $"{at}<{argument.ReadableName()}>", problems, seen);
            return;
        }

        if (type.IsInterface || type.IsAbstract)
        {
            problems.Add(
                $"'{at}' is typed {type.ReadableName()}, an interface or abstract type, so "
                    + "nothing says what to read it back as. Give it a sealed concrete type."
            );
            return;
        }

        if (type.IsClass && !type.IsSealed)
            problems.Add(
                $"'{at}' is {type.ReadableName()}, which is not sealed, so a derived value would "
                    + "be read back as the base type. Seal it."
            );

        var parameters = type.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.Name)
            .Where(n => n is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (
            var t = type;
            t is not null && t != typeof(object) && t != typeof(ValueType);
            t = t.BaseType
        )
        {
            foreach (
                var field in t.GetFields(
                    BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.DeclaredOnly
                )
            )
            {
                if (BackedProperty(t, field) is { } property)
                {
                    if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
                        problems.Add(
                            $"'{at}.{property.Name}' is ignored by the serializer, so it is lost "
                                + "when the state is stored. Remove the [JsonIgnore] or the property."
                        );
                    else if (property.GetMethod?.IsPublic != true)
                        problems.Add(
                            $"'{at}.{property.Name}' has no public getter, so it is not stored. "
                                + "Make it public."
                        );
                    else if (
                        property.SetMethod?.IsPublic != true
                        && !parameters.Contains(property.Name)
                    )
                        problems.Add(
                            $"'{at}.{property.Name}' has no public setter or init accessor and no "
                                + "constructor parameter of its name, so it is not read back."
                        );

                    continue;
                }

                if (field.GetCustomAttribute<CompilerGeneratedAttribute>() is not null)
                    continue;

                problems.Add(
                    $"'{at}' holds the field '{field.Name}', which the serializer skips, so it "
                        + "is lost when the state is stored. Make it a property."
                );
            }
        }

        foreach (
            var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetIndexParameters().Length == 0)
                .Where(p => p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
        )
            Visit(property.PropertyType, $"{at}.{property.Name}", problems, seen);
    }

    /// <summary>The auto-property <paramref name="field"/> backs, or null for a field of its own.</summary>
    private static PropertyInfo? BackedProperty(Type declaring, FieldInfo field) =>
        field.Name.StartsWith('<')
        && field.Name.EndsWith(">k__BackingField", StringComparison.Ordinal)
            ? declaring.GetProperty(
                field.Name[1..field.Name.IndexOf('>')],
                BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.DeclaredOnly
            )
            : null;

    /// <summary>
    /// A collection the serializer knows how to read back from its elements alone: arrays are
    /// handled separately, and these are the framework's own generic collections and the
    /// interfaces it reads into one.
    /// </summary>
    private static bool IsFrameworkCollection(Type type) =>
        type.IsGenericType
        && type.Namespace
            is "System.Collections.Generic"
                or "System.Collections.Immutable"
                or "System.Collections.ObjectModel"
        && type.GetInterfaces()
            .Append(type)
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
}
