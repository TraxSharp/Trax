using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Trax.Effect.StateMachine;

/// <summary>
/// Builds a <see cref="ContextSchema"/> from the author's C# context record: the field names come from the
/// properties (PascalCase -> camelCase), the JSON types from the property types, and nullability from the
/// C# nullable annotation (<c>string?</c>) or a nullable value type (<c>int?</c>). This is what lets the
/// author write a record and get the schema for free, no field-name strings.
/// </summary>
public static class SchemaReflection
{
    public static ContextSchema For<T>() => For(typeof(T));

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
                    JsonTypeOf(p.PropertyType),
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

    // The JSON type of a typed collection's elements (int[] -> Number, string[] -> String), or null when the
    // property is not a scalar-element collection (a string, a scalar, or an array of objects).
    private static JsonFieldType? ArrayElementType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
            return null;

        var element =
            type.IsArray ? type.GetElementType()
            : type.IsGenericType ? type.GetGenericArguments().FirstOrDefault()
            : null;
        if (element is null)
            return null;
        element = Nullable.GetUnderlyingType(element) ?? element;

        if (element == typeof(string))
            return JsonFieldType.String;
        if (element == typeof(bool))
            return JsonFieldType.Boolean;
        if (IsNumeric(element))
            return JsonFieldType.Number;
        return null; // arrays of objects: the field is Array, but elements aren't further type-checked
    }

    private static JsonFieldType JsonTypeOf(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string))
            return JsonFieldType.String;
        if (type == typeof(bool))
            return JsonFieldType.Boolean;
        if (IsNumeric(type))
            return JsonFieldType.Number;
        if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
            return JsonFieldType.Array;
        return JsonFieldType.Object;
    }

    private static bool IsNumeric(Type type) =>
        type == typeof(int)
        || type == typeof(long)
        || type == typeof(short)
        || type == typeof(byte)
        || type == typeof(double)
        || type == typeof(float)
        || type == typeof(decimal);

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
