using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Trax.Effect.StateMachine;

/// <summary>
/// Builds a <see cref="ContextSchema"/> from the author's C# record: the field names come from the properties
/// (their <c>[JsonPropertyName]</c>, else PascalCase -> camelCase), the JSON types from the property types
/// (an integral CLR number is <see cref="JsonFieldType.Integer"/>), and nullability from the C# nullable
/// annotation (<c>string?</c>) or a nullable value type (<c>int?</c>). A <c>[JsonIgnore]</c> property is not
/// part of the schema. This is what lets the author write a record and get the schema for free, no field-name
/// strings.
///
/// <para>Each field's JSON type is the one System.Text.Json writes for its CLR type, so a record always
/// validates against its own serialized form, with one exception: an integer within one double step of a 64-bit
/// (or wider) type's end, which an engine reading JSON numbers as doubles cannot tell from one past the end, is
/// refused with it. An integral field carries its CLR range, and a dictionary's values and a list's elements
/// carry their own schema. A property whose type has no fixed JSON type (<c>object</c>,
/// a raw <c>JsonNode</c>, a <c>[Flags]</c> enum) throws <see cref="InvalidOperationException"/> here, when the machine is
/// configured, rather than being given a type its values would not match.</para>
///
/// <para>A trigger's input and an invoked train's output (<see cref="ForInput"/>) are described as strictly as
/// their CLR type: a nested record and the elements of a list of records carry their own field shapes, and a
/// type that contains itself is refused, since it has no finite shape to validate against, and a property
/// System.Text.Json never sets on read (a computed one) is not one of its fields. A context
/// (<see cref="For(Type)"/>) is described one level deep.</para>
///
/// <para>Infrastructure behind <c>Context&lt;T&gt;()</c> and <c>WithInput&lt;T&gt;()</c>; not intended to be
/// called directly.</para>
/// </summary>
internal static class SchemaReflection
{
    /// <summary>Builds the schema for <typeparamref name="T"/>; see <see cref="For(Type)"/>.</summary>
    public static ContextSchema For<T>() => For(typeof(T));

    /// <summary>
    /// Builds a context schema from <paramref name="contextType"/>'s public readable instance properties, with
    /// fields sorted by their JSON name. Its constraints read the context.
    /// </summary>
    /// <param name="contextType">The context record type.</param>
    /// <exception cref="InvalidOperationException">A property's type has no fixed JSON type.</exception>
    public static ContextSchema For(Type contextType)
    {
        var schema = new ContextSchema(Reflect(contextType, RuleSource.Context, stack: null));
        RefuseCycles(contextType, [contextType]);
        return schema;
    }

    // A context is described one level deep, but a record that contains itself has no finite shape for any
    // generator to describe either, so it is refused like a recursive input.
    private static void RefuseCycles(Type type, List<Type> stack)
    {
        foreach (
            var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !Ignored(p))
        )
        {
            var propertyType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            var record =
                IsRecord(propertyType) ? propertyType
                : typeof(IEnumerable).IsAssignableFrom(propertyType)
                && !IsDictionary(propertyType)
                && propertyType != typeof(string)
                    ? ElementType(propertyType)
                : null;
            if (record is null)
                continue;
            record = Nullable.GetUnderlyingType(record) ?? record;
            if (!IsRecord(record))
                continue;
            if (stack.Contains(record))
                throw new InvalidOperationException(
                    $"{type.Name}.{p.Name} is a '{record.Name}', which contains itself, so '{stack[0].Name}' "
                        + "has no finite shape. Give it a shape that ends: replace the recursive member with an "
                        + "id the server resolves."
                );
            stack.Add(record);
            RefuseCycles(record, stack);
            stack.RemoveAt(stack.Count - 1);
        }
    }

    /// <summary>
    /// Builds the schema of a trigger's input or an invoked train's output from <paramref name="inputType"/>:
    /// nested records and lists of records carry their own field shapes, and the constraints read the input.
    /// </summary>
    /// <param name="inputType">The input or output record type.</param>
    /// <exception cref="InvalidOperationException">
    /// A property's type has no fixed JSON type, or the type contains itself.
    /// </exception>
    public static ContextSchema ForInput(Type inputType) =>
        new(Reflect(inputType, RuleSource.Input, stack: [inputType]));

    // The fields of one record. With a stack (an input), each record-typed field also gets the shape of its
    // record, and the stack is the chain of records being reflected, so a type reached again is a cycle; and a
    // property System.Text.Json never sets (a computed one) is left out, so a client sending it is refused.
    private static List<FieldSchema> Reflect(Type type, RuleSource source, List<Type>? stack) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p =>
                p.CanRead
                && p.GetIndexParameters().Length == 0
                && !Ignored(p)
                && (stack is null || IsReadBack(type, p))
            )
            .Select(p =>
            {
                var name = MemberPath.JsonNameOf(p);
                var member = $"{type.Name}.{p.Name}";
                var jsonType = JsonTypeOf(p.PropertyType, member);
                var (minimum, maximum) = Bounds(p.PropertyType);
                return new FieldSchema(name, jsonType, IsNullable(p), Constraints(p, name, source))
                {
                    Fields = stack is null ? null : Shape(p.PropertyType, jsonType, stack, member),
                    Values = ValuesOf(p.PropertyType, Nullability(p), stack, member),
                    Keys = KeysOf(p.PropertyType, member),
                    Minimum = minimum,
                    Maximum = maximum,
                    Enum = EnumOf(p.PropertyType),
                    EnumExact = ExactEnumOf(p.PropertyType),
                    Format = FormatOf(p.PropertyType),
                };
            })
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Whether System.Text.Json sets <paramref name="property"/> when it reads a <paramref name="type"/>: through a
    /// public setter (an <c>init</c> one too), a setter marked <c>[JsonInclude]</c>, or a parameter of the
    /// constructor it reads the type with. A computed property (<c>int Total =&gt; Qty * Price</c>) is none of
    /// these: it is skipped on read, so an input carrying it carries a value the record never holds.
    /// </summary>
    internal static bool IsReadBack(Type type, PropertyInfo property)
    {
        if (property.GetSetMethod() is not null)
            return true;
        if (property.SetMethod is not null && property.IsDefined(typeof(JsonIncludeAttribute)))
            return true;
        return ReadingConstructor(type) is { } constructor
            && constructor
                .GetParameters()
                .Any(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));
    }

    // The constructor System.Text.Json reads a type with: the one marked [JsonConstructor], else none when the type
    // has a public parameterless one (or is a struct), else its one public constructor.
    private static ConstructorInfo? ReadingConstructor(Type type)
    {
        var marked = type.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            )
            .FirstOrDefault(c => c.IsDefined(typeof(JsonConstructorAttribute)));
        if (marked is not null)
            return marked;
        var constructors = type.GetConstructors();
        if (type.IsValueType || constructors.Any(c => c.GetParameters().Length == 0))
            return null;
        return constructors.Length == 1 ? constructors[0] : null;
    }

    /// <summary>
    /// A System.Text.Json contract modifier that leaves out of what it writes every property
    /// <see cref="IsReadBack"/> says reading never sets, so a record written as an input carries only the fields
    /// its input schema has.
    /// </summary>
    internal static void LeaveOutComputedProperties(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object)
            return;
        for (var i = info.Properties.Count - 1; i >= 0; i--)
            if (
                info.Properties[i].AttributeProvider is PropertyInfo property
                && !IsReadBack(info.Type, property)
            )
                info.Properties.RemoveAt(i);
    }

    // System.Text.Json never writes a member marked [JsonIgnore] (its default condition, Always), so it is not
    // part of what a client sends or a train returns.
    private static bool Ignored(PropertyInfo property) =>
        property.GetCustomAttribute<JsonIgnoreAttribute>()
            is { Condition: JsonIgnoreCondition.Always };

    // The field shape of a record-typed field, or of the elements of a list of records; null for anything else
    // (a scalar, a list of scalars, a dictionary).
    private static IReadOnlyList<FieldSchema>? Shape(
        Type type,
        JsonFieldType jsonType,
        List<Type> stack,
        string member
    )
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        var record = jsonType switch
        {
            JsonFieldType.Object => type,
            JsonFieldType.Array => ElementType(type),
            _ => null,
        };
        if (record is null)
            return null;
        record = Nullable.GetUnderlyingType(record) ?? record;
        if (!IsRecord(record))
            return null;

        if (stack.Contains(record))
            throw new InvalidOperationException(
                $"{member} is a '{record.Name}', which contains itself, so '{stack[0].Name}' has no finite shape "
                    + "to validate an input against. Give the input a shape that ends: replace the recursive "
                    + "member with a list of records, or with an id the server resolves."
            );

        stack.Add(record);
        try
        {
            return Reflect(record, RuleSource.Input, stack);
        }
        finally
        {
            stack.RemoveAt(stack.Count - 1);
        }
    }

    // The schema every value of a dictionary, or every element of a list whose elements are not records, is
    // checked against; null for anything else, and for values with no fixed JSON type. A list of records carries
    // its element shape as Fields instead.
    // The value's nullability comes from its C# annotation where the declared type exposes one
    // (Dictionary<string, string?>); a reference-typed value whose annotation cannot be read is not nullable.
    private static FieldSchema? ValuesOf(
        Type type,
        NullabilityInfo? nullability,
        List<Type>? stack,
        string member
    )
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        Type? value;
        NullabilityInfo? valueNullability;
        if (IsDictionary(type))
        {
            if (type == typeof(JsonObject))
                return null;
            value = DictionaryValueType(type);
            valueNullability =
                type.IsGenericType && nullability?.GenericTypeArguments.Length == 2
                    ? nullability.GenericTypeArguments[1]
                    : null;
        }
        else if (
            type != typeof(string)
            && ScalarTypeOf(type) is null
            && !Unmappable(type)
            && type != typeof(JsonObject)
            && typeof(IEnumerable).IsAssignableFrom(type)
            && ElementType(type) is { } element
            && !IsRecord(Nullable.GetUnderlyingType(element) ?? element)
        )
        {
            value = element;
            valueNullability =
                nullability?.ElementType
                ?? (
                    nullability?.GenericTypeArguments.Length == 1
                        ? nullability.GenericTypeArguments[0]
                        : null
                );
        }
        else
            return null;

        // Values with no fixed JSON type (a Hashtable's, an object[]'s, a JsonNode list's) are any JSON, as a
        // JsonObject field's are.
        if (
            value is null
            || (
                ScalarTypeOf(Nullable.GetUnderlyingType(value) ?? value) is null
                && Unmappable(Nullable.GetUnderlyingType(value) ?? value)
            )
        )
            return null;

        var path = member + "[]";
        var jsonType = JsonTypeOf(value, path);
        var (minimum, maximum) = Bounds(value);
        return new FieldSchema(string.Empty, jsonType, IsNullable(value, valueNullability), [])
        {
            Fields = stack is null ? null : Shape(value, jsonType, stack, path),
            Values = ValuesOf(value, valueNullability, stack, path),
            Keys = KeysOf(value, path),
            Minimum = minimum,
            Maximum = maximum,
            Enum = EnumOf(value),
            EnumExact = ExactEnumOf(value),
            Format = FormatOf(value),
        };
    }

    // The schema a dictionary's keys are checked against, from its key type, as System.Text.Json reads a key of that
    // type: an integer's digits, an enum's names, or a Guid's, a date's, a time's or a char's format. Null for a
    // dictionary keyed by string (or with untyped keys) and for anything else. A key type with no such reading (a
    // TimeSpan, whose ISO-8601 converter reads no key, a bool, a fractional number, a record) is refused.
    private static FieldSchema? KeysOf(Type type, string member)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (!IsDictionary(type) || DictionaryKeyType(type) is not { } key || key == typeof(string))
            return null;
        if (
            IsFlags(key)
            || !(
                IsIntegral(key)
                || key.IsEnum
                || ValueFormats.Of(key)
                    is { }
                        and not ValueFormats.Duration
                        and not ValueFormats.Uri
            )
        )
            throw new InvalidOperationException(
                $"{member} is a dictionary keyed by '{key.Name}', which no schema checks a key of. Key it by a "
                    + "string, an integer, an enum without [Flags], a Guid, a date, a time or a char."
            );
        var (minimum, maximum) = Bounds(key);
        return new FieldSchema(
            string.Empty,
            IsIntegral(key) ? JsonFieldType.Integer : JsonFieldType.String,
            Nullable: false,
            []
        )
        {
            Minimum = minimum,
            Maximum = maximum,
            Enum = EnumOf(key),
            EnumExact = ExactEnumOf(key),
            Format = FormatOf(key),
        };
    }

    // TKey of the IDictionary<TKey, TValue> or IReadOnlyDictionary<TKey, TValue> a type implements, or null for a
    // dictionary with untyped keys (a Hashtable).
    private static Type? DictionaryKeyType(Type type) =>
        type.GetInterfaces()
            .Append(type)
            .Where(i =>
                i.IsGenericType
                && (
                    i.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                    || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                )
            )
            .Select(i => i.GetGenericArguments()[0])
            .FirstOrDefault();

    // TValue of the IDictionary<TKey, TValue> or IReadOnlyDictionary<TKey, TValue> a type implements, or null for
    // a dictionary with untyped values (a Hashtable).
    private static Type? DictionaryValueType(Type type) =>
        type.GetInterfaces()
            .Append(type)
            .Where(i =>
                i.IsGenericType
                && (
                    i.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                    || i.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)
                )
            )
            .Select(i => i.GetGenericArguments()[1])
            .FirstOrDefault();

    // The names an enum's JSON string may hold; null for a type that is not an enum.
    private static IReadOnlyList<string>? EnumOf(Type type) =>
        ValueFormats.EnumNames(Nullable.GetUnderlyingType(type) ?? type);

    // The names of an enum that match only as written; null when it renames none, or is not an enum.
    private static IReadOnlyList<string>? ExactEnumOf(Type type) =>
        ValueFormats.ExactEnumNames(Nullable.GetUnderlyingType(type) ?? type);

    // The shape a scalar's JSON string has; null for a type whose string has none.
    private static string? FormatOf(Type type) =>
        ValueFormats.Of(Nullable.GetUnderlyingType(type) ?? type);

    // The range of a CLR number type, as the doubles both engines compare a JSON number against: an integral
    // type's, a decimal's, a float's and a Half's. A double has none beyond being finite. A JSON
    // number reaches both engines as a double, and past 2^53 two integers can share one, so a bound that far
    // out is the nearest double strictly inside the CLR range: every number that rounds to it or nearer fits
    // the type. Values within one rounding step of a 64-bit (or wider) type's ends are refused with the values
    // just past them, which neither engine can tell apart. A stored number is written as JavaScript writes it,
    // with an exponent from 1e21 on, and an Int128 or UInt128 does not read one, so those two stop short of 1e21.
    // A float's and a Half's bound is the larger of the type's maximum and the shortest text that reads back as
    // it (3.4028235E+38, which System.Text.Json writes for float.MaxValue), so the maximum round-trips. Null for
    // any other type.
    private static (double? Minimum, double? Maximum) Bounds(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        (BigInteger Min, BigInteger Max)? range =
            type == typeof(int) ? (int.MinValue, int.MaxValue)
            : type == typeof(long) ? (long.MinValue, long.MaxValue)
            : type == typeof(short) ? (short.MinValue, short.MaxValue)
            : type == typeof(byte) ? (byte.MinValue, byte.MaxValue)
            : type == typeof(sbyte) ? (sbyte.MinValue, sbyte.MaxValue)
            : type == typeof(uint) ? (uint.MinValue, uint.MaxValue)
            : type == typeof(ulong) ? (ulong.MinValue, ulong.MaxValue)
            : type == typeof(ushort) ? (ushort.MinValue, ushort.MaxValue)
            : type == typeof(Int128) ? (-ExponentFree, ExponentFree)
            : type == typeof(UInt128) ? (BigInteger.Zero, ExponentFree)
            : type == typeof(decimal) ? ((BigInteger)decimal.MinValue, (BigInteger)decimal.MaxValue)
            : null;
        if (range is null && type == typeof(float))
            return Symmetric(
                Math.Max(
                    float.MaxValue,
                    Shortest(float.MaxValue.ToString(CultureInfo.InvariantCulture))
                )
            );
        if (range is null && type == typeof(Half))
            return Symmetric(
                Math.Max(
                    (double)Half.MaxValue,
                    Shortest(Half.MaxValue.ToString(CultureInfo.InvariantCulture))
                )
            );
        if (range is not { } r)
            return (null, null);
        return (Inside(r.Min, upper: false), Inside(r.Max, upper: true));
    }

    // 10^21, from which JavaScript (and so the stored wire) writes an integer with an exponent. Int128 and UInt128
    // are bounded strictly inside it.
    private static readonly BigInteger ExponentFree = BigInteger.Pow(10, 21);

    private static double Shortest(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    private static (double?, double?) Symmetric(double max) => (-max, max);

    private static readonly BigInteger ExactDoubles = BigInteger.Pow(2, 53);

    private static double Inside(BigInteger bound, bool upper)
    {
        var value = (double)bound;
        if (BigInteger.Abs(bound) <= ExactDoubles)
            return value;
        while (upper ? new BigInteger(value) >= bound : new BigInteger(value) <= bound)
            value = upper ? Math.BitDecrement(value) : Math.BitIncrement(value);
        return value;
    }

    private static Type? ElementType(Type type) =>
        type.IsArray ? type.GetElementType()
        : type.IsGenericType ? type.GetGenericArguments().FirstOrDefault()
        : null;

    // A record or class written as a JSON object of its properties: not a scalar, a collection, a dictionary or
    // a raw JSON type.
    private static bool IsRecord(Type type) =>
        ScalarTypeOf(type) is null
        && !Unmappable(type)
        && !IsDictionary(type)
        && type != typeof(JsonObject)
        && !typeof(IEnumerable).IsAssignableFrom(type);

    // Maps a property's type and validation attributes to declarative constraint rules over the field:
    // [MinLength(>=1)] -> non-empty, [MaxLength(n)] -> a length or count of at most n, a typed collection ->
    // every element is that JSON type, [AllowedValues]
    // -> one of a fixed set (the enum domain). More can be added as real machines need them. Each constraint
    // names the field in the record being validated, and reads it from where that record lives: the context
    // for a context schema, the input for an input's.
    private static IReadOnlyList<Rule> Constraints(
        PropertyInfo property,
        string jsonName,
        RuleSource source
    )
    {
        var rules = new List<Rule>();
        if (property.GetCustomAttribute<MinLengthAttribute>() is { Length: >= 1 })
            rules.Add(new Rule.NonEmpty(source, jsonName));
        // [MaxLength(n)] bounds a string's length or a collection's count, checked only when the value is present.
        if (property.GetCustomAttribute<MaxLengthAttribute>() is { Length: >= 0 } max)
            rules.Add(
                property.PropertyType != typeof(string)
                && typeof(IEnumerable).IsAssignableFrom(property.PropertyType)
                    ? new Rule.Count(source, jsonName, CompareOp.LessOrEqual, max.Length)
                    : new Rule.Length(source, jsonName, CompareOp.LessOrEqual, max.Length)
            );
        if (ArrayElementType(property.PropertyType) is { } element)
            rules.Add(new Rule.ArrayOf(source, jsonName, element));
        if (property.GetCustomAttribute<AllowedValuesAttribute>() is { Values.Length: > 0 } allowed)
            rules.Add(
                new Rule.OneOf(
                    source,
                    jsonName,
                    allowed.Values.Select(v => v?.ToString() ?? string.Empty).ToArray()
                )
            );
        return rules;
    }

    // The JSON type of a typed collection's elements (int[] -> Integer, Guid[] -> String), or null when the
    // property is not a scalar-element collection (a string, a scalar, a dictionary, or an array of objects).
    private static JsonFieldType? ArrayElementType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (JsonTypeOf(type) != JsonFieldType.Array)
            return null;

        var element = ElementType(type);
        if (element is null)
            return null;

        return ScalarTypeOf(Nullable.GetUnderlyingType(element) ?? element);
    }

    private static JsonFieldType JsonTypeOf(Type type, string? member = null)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (IsFlags(type))
            throw new InvalidOperationException(
                $"{member ?? "A field"} is the [Flags] enum '{type.Name}', which System.Text.Json writes as a "
                    + "list of names, or as a number when no names cover the value, so it has no fixed JSON type "
                    + "and no schema can be reflected for it. Declare the field as a list of an enum without "
                    + "[Flags], one member per flag."
            );
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
    // differential inputs and a camelCase API use. An integral CLR number is a JSON integer, so a fraction is
    // refused where the CLR type could not hold it; every other CLR number type is a JSON number.
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
        if (IsIntegral(type))
            return JsonFieldType.Integer;
        if (IsFractional(type))
            return JsonFieldType.Number;
        return null;
    }

    private static bool IsFlags(Type type) =>
        type.IsEnum && type.IsDefined(typeof(FlagsAttribute), inherit: false);

    private static bool IsIntegral(Type type) =>
        type == typeof(int)
        || type == typeof(long)
        || type == typeof(short)
        || type == typeof(byte)
        || type == typeof(sbyte)
        || type == typeof(uint)
        || type == typeof(ulong)
        || type == typeof(ushort)
        || type == typeof(Int128)
        || type == typeof(UInt128);

    private static bool IsFractional(Type type) =>
        type == typeof(Half)
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

    private static bool IsNullable(PropertyInfo property) =>
        IsNullable(property.PropertyType, Nullability(property));

    private static NullabilityInfo Nullability(PropertyInfo property) =>
        new NullabilityInfoContext().Create(property);

    private static bool IsNullable(Type type, NullabilityInfo? info)
    {
        if (Nullable.GetUnderlyingType(type) is not null)
            return true; // int?, bool?, ...

        if (type.IsValueType)
            return false;

        // Reference type: read the C# nullable annotation (string? vs string).
        return info?.ReadState == NullabilityState.Nullable;
    }
}
