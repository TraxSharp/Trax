using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Trax.Client;

/// <summary>The JSON options a generated client reads and writes with.</summary>
public static class TraxJson
{
    /// <summary>
    /// Web defaults (camelCase, case-insensitive reads), with every <see cref="Optional{T}"/> property left out of the
    /// JSON when it holds no value. Generated records name each property with <c>[JsonPropertyName]</c> and each enum
    /// value with <c>[JsonStringEnumMemberName]</c>, so nothing depends on the naming policy.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { LeaveOutUnset } },
        };
        options.MakeReadOnly();
        return options;
    }

    // What a client reads and writes with: a copy of the caller's options that leaves unset Optionals out too,
    // whatever resolver they read types with, or Options when there are none.
    internal static JsonSerializerOptions LeavingOutUnset(JsonSerializerOptions? options)
    {
        if (options is null || ReferenceEquals(options, Options))
            return Options;
        var copy = new JsonSerializerOptions(options)
        {
            TypeInfoResolver = (
                options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver()
            ).WithAddedModifier(LeaveOutUnset),
        };
        copy.MakeReadOnly();
        return copy;
    }

    private static void LeaveOutUnset(JsonTypeInfo type)
    {
        if (type.Kind != JsonTypeInfoKind.Object)
            return;
        foreach (var property in type.Properties)
            if (
                property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition() == typeof(Optional<>)
            )
                property.ShouldSerialize = static (_, value) =>
                    value is IOptional { HasValue: true };
    }
}
