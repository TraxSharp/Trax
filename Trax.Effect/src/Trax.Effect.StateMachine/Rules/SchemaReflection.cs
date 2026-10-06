using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>
/// Builds a <see cref="ContextSchema"/> from the author's C# context record: the field names come from the
/// properties (PascalCase -> camelCase), the JSON types from the property types, and nullability from the
/// C# nullable annotation (<c>string?</c>) or a nullable value type (<c>int?</c>). This is what lets the
/// author write a record and get the schema for free, no field-name strings.
///
/// <para>Each field's JSON type is the one System.Text.Json writes for its CLR type, so a record always
/// validates against its own serialized form. A property whose type has no fixed JSON type (<c>object</c>,
/// a raw <c>JsonNode</c>) throws <see cref="InvalidOperationException"/> here, when the machine is
/// configured, rather than being given a type its values would not match.</para>
///
/// <para>Infrastructure behind <c>Context&lt;T&gt;()</c> and <c>WithInput&lt;T&gt;()</c>; not intended to be
/// called directly.</para>
/// </summary>
internal static class SchemaReflection
{
    /// <summary>Builds the schema for <typeparamref name="T"/>; see <see cref="For(Type)"/>.</summary>
    public static ContextSchema For<T>() => For(typeof(T));

    /// <summary>
    /// Builds the schema from <paramref name="contextType"/>'s public readable instance properties, with
    /// fields sorted by their camelCase JSON name.
    /// </summary>
    /// <param name="contextType">The context or input record type.</param>
    /// <exception cref="InvalidOperationException">A property's type has no fixed JSON type.</exception>
    public static ContextSchema For(Type contextType)
    {
        var fields = contextType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p =>
            {
                var name = MemberPath.ToJsonName(p.Name);
                return new FieldSchema(
                    name,
                    JsonTypeOf(p.PropertyType, $"{contextType.Name}.{p.Name}"),
                    IsNullable(p),
                    Constraints(p, name)
                );
            })
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

        return new ContextSchema(fields);
    }

    // Maps a property's type and validation attributes to declarative constraint rules over the field:
    // [MinLength(>=1)] -> non-empty, a typed collection -> every element is that JSON type, [AllowedValues]
    // -> one of a fixed set (the enum domain). More can be added as real machines need them.
    private static IReadOnlyList<Rule> Constraints(PropertyInfo property, string jsonName)
    {
        var rules = new List<Rule>();
        if (property.GetCustomAttribute<MinLengthAttribute>() is { Length: >= 1 })
            rules.Add(new Rule.NonEmpty(RuleSource.Context, jsonName));
        if (ArrayElementType(property.PropertyType) is { } element)
            rules.Add(new Rule.ArrayOf(RuleSource.Context, jsonName, element));
        if (property.GetCustomAttribute<AllowedValuesAttribute>() is { Values.Length: > 0 } allowed)
            rules.Add(
                new Rule.OneOf(
                    RuleSource.Context,
                    jsonName,
                    allowed.Values.Select(v => v?.ToString() ?? string.Empty).ToArray()
                )
            );
        return rules;
    }

    // The JSON type of a typed collection's elements (int[] -> Number, Guid[] -> String), or null when the
    // property is not a scalar-element collection (a string, a scalar, a dictionary, or an array of objects).
    private static JsonFieldType? ArrayElementType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (JsonTypeOf(type) != JsonFieldType.Array)
            return null;

        var element =
            type.IsArray ? type.GetElementType()
            : type.IsGenericType ? type.GetGenericArguments().FirstOrDefault()
            : null;
        if (element is null)
            return null;

        return ScalarTypeOf(Nullable.GetUnderlyingType(element) ?? element);
    }

    private static JsonFieldType JsonTypeOf(Type type, string? member = null)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (ScalarTypeOf(type) is { } scalar)
            return scalar;
        if (Unmappable(type))
            throw new InvalidOperationException(
                $"{member ?? "A field"} is a '{type}', which has no fixed JSON type, so no schema can be "
                    + "reflected for it. Give the field a type that serializes to one JSON type (a string, "
                    + "number, boolean, collection, dictionary or record), or validate the state with Holds(...)."
            );
        // A dictionary serializes as a JSON object, so it is checked before the IEnumerable it also is.
        if (IsDictionary(type) || type == typeof(JsonObject))
            return JsonFieldType.Object;
        if (typeof(IEnumerable).IsAssignableFrom(type))
            return JsonFieldType.Array;
        return JsonFieldType.Object; // a record or class: a nested JSON object
    }

    // The JSON type System.Text.Json writes for a scalar, or null for a type that is not one. Guids, dates,
    // times, URIs and chars are strings; enums are strings too, under the JsonStringEnumConverter the
    // differential inputs and a camelCase API use. Every CLR number type is a JSON number.
    private static JsonFieldType? ScalarTypeOf(Type type)
    {
        if (
            type == typeof(string)
            || type == typeof(char)
            || type == typeof(Guid)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(DateOnly)
            || type == typeof(TimeOnly)
            || type == typeof(TimeSpan)
            || type == typeof(Uri)
            || type.IsEnum
        )
            return JsonFieldType.String;
        if (type == typeof(bool))
            return JsonFieldType.Boolean;
        if (IsNumeric(type))
            return JsonFieldType.Number;
        return null;
    }

    private static bool IsNumeric(Type type) =>
        type == typeof(int)
        || type == typeof(long)
        || type == typeof(short)
        || type == typeof(byte)
        || type == typeof(sbyte)
        || type == typeof(uint)
        || type == typeof(ulong)
        || type == typeof(ushort)
        || type == typeof(Int128)
        || type == typeof(UInt128)
        || type == typeof(Half)
        || type == typeof(double)
        || type == typeof(float)
        || type == typeof(decimal);

    private static bool IsDictionary(Type type) =>
        typeof(IDictionary).IsAssignableFrom(type)
        || type.GetInterfaces()
            .Append(type)
            .Any(i =>
                i.IsGenericType
                && (
                    i.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                    || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                )
            );

    // Types whose JSON shape is not fixed by the type (object, a raw JsonNode or JsonElement), that
    // System.Text.Json writes as something other than their value (BigInteger), or that are not data at
    // all. Reflecting "object" for them would give the field a type its own values do not have.
    private static bool Unmappable(Type type) =>
        type == typeof(object)
        || type == typeof(JsonNode)
        || type == typeof(JsonValue)
        || type == typeof(JsonElement)
        || type == typeof(JsonDocument)
        || type == typeof(BigInteger)
        || type.IsPrimitive // IntPtr, UIntPtr: every primitive with a JSON type is mapped above
        || type.IsPointer
        || typeof(Delegate).IsAssignableFrom(type)
        || typeof(MemberInfo).IsAssignableFrom(type);

    private static bool IsNullable(PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
            return true; // int?, bool?, ...

        if (property.PropertyType.IsValueType)
            return false;

        // Reference type: read the C# nullable annotation (string? vs string).
        var info = new NullabilityInfoContext().Create(property);
        return info.ReadState == NullabilityState.Nullable;
    }
}
