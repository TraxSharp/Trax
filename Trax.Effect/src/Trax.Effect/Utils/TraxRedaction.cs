using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Trax.Effect.Attributes;

namespace Trax.Effect.Utils;

/// <summary>
/// Serializer options that write a <see cref="TraxSensitiveAttribute"/> member as
/// <c>{"_redacted": true}</c>, for every copy of a train's input or output that Trax keeps.
/// </summary>
public static class TraxRedaction
{
    /// <summary>
    /// The property of the object that stands in for a masked value.
    /// </summary>
    public const string MarkerProperty = "_redacted";

    private static readonly ConditionalWeakTable<
        JsonSerializerOptions,
        JsonSerializerOptions
    > Derived = new();

    /// <summary>
    /// Returns options that serialize exactly as <paramref name="options"/> do, except that every
    /// member marked <see cref="TraxSensitiveAttribute"/> is written as <c>{"_redacted": true}</c>.
    /// </summary>
    /// <remarks>
    /// The same instance is returned for the same <paramref name="options"/>, so System.Text.Json's
    /// per-options metadata cache survives across calls. <paramref name="options"/> itself is not
    /// changed. The returned options are for writing: reading a masked value back throws
    /// <see cref="JsonException"/>, because the mask is not the value.
    /// </remarks>
    public static JsonSerializerOptions WithRedaction(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Derived.GetValue(options, Derive);
    }

    /// <summary>
    /// Whether <paramref name="json"/> contains a masked value anywhere, which means it cannot be
    /// read back as the input or output it was written from. Text that is not JSON has none.
    /// </summary>
    public static bool ContainsRedaction(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (!json.Contains(MarkerProperty, StringComparison.Ordinal))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            return ContainsMarker(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ContainsMarker(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (
                        property.NameEquals(MarkerProperty)
                        && property.Value.ValueKind == JsonValueKind.True
                    )
                        return true;
                    if (ContainsMarker(property.Value))
                        return true;
                }
                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    if (ContainsMarker(item))
                        return true;
                return false;
            default:
                return false;
        }
    }

    private static JsonSerializerOptions Derive(JsonSerializerOptions options)
    {
        var resolver = options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver();
        var derived = new JsonSerializerOptions(options)
        {
            TypeInfoResolver = resolver.WithAddedModifier(MaskSensitiveMembers),
        };
        derived.MakeReadOnly();
        return derived;
    }

    private static void MaskSensitiveMembers(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        foreach (var property in typeInfo.Properties)
            if (IsSensitive(typeInfo.Type, property))
                property.CustomConverter = (JsonConverter)
                    Activator.CreateInstance(
                        typeof(RedactingConverter<>).MakeGenericType(property.PropertyType)
                    )!;
    }

    /// <summary>
    /// Marked on the member itself (or a base declaration of it), on the constructor parameter it
    /// binds to (a positional record's parameter), or on an interface member it implements.
    /// </summary>
    private static bool IsSensitive(Type declaringType, JsonPropertyInfo property)
    {
        if (
            property.AttributeProvider is MemberInfo member
            && Attribute.IsDefined(member, typeof(TraxSensitiveAttribute), inherit: true)
        )
            return true;

        if (
            property.AssociatedParameter?.AttributeProvider is ParameterInfo parameter
            && parameter.IsDefined(typeof(TraxSensitiveAttribute), inherit: false)
        )
            return true;

        if (property.AttributeProvider is not MemberInfo clrMember)
            return false;

        // A record's primary constructor parameter, when System.Text.Json did not bind to that
        // constructor (another one was chosen, or the type is only ever written).
        foreach (var constructor in declaringType.GetConstructors())
        foreach (var candidate in constructor.GetParameters())
            if (
                string.Equals(candidate.Name, clrMember.Name, StringComparison.Ordinal)
                && candidate.IsDefined(typeof(TraxSensitiveAttribute), inherit: false)
            )
                return true;

        foreach (var contract in declaringType.GetInterfaces())
        {
            var declared = contract.GetProperty(clrMember.Name);
            if (declared is not null && declared.IsDefined(typeof(TraxSensitiveAttribute), false))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Writes the stand-in object whatever the value is, and refuses to read one back.
    /// </summary>
    private sealed class RedactingConverter<T> : JsonConverter<T>
    {
        public override T Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) =>
            throw new JsonException(
                "A value masked by [TraxSensitive] cannot be read back; the stored copy does not "
                    + "hold it."
            );

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean(MarkerProperty, true);
            writer.WriteEndObject();
        }
    }
}
