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

    // Maps validation attributes to declarative constraint rules over the field. Kept small on purpose:
    // [MinLength(>=1)] on a string/array is the non-empty constraint the real machines use. More can be
    // added as real machines need them.
    private static IReadOnlyList<Rule> Constraints(PropertyInfo property, string jsonName)
    {
        var rules = new List<Rule>();
        if (property.GetCustomAttribute<MinLengthAttribute>() is { Length: >= 1 })
            rules.Add(new Rule.NonEmpty(RuleSource.Context, jsonName));
        return rules;
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
