using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Trax.Client;

/// <summary>The JSON options a generated client reads and writes with.</summary>
public static class TraxJson
{
    private const string ByReflection =
        "TraxJson.Options reads types by reflection. A trimmed or native AOT app passes TraxClientOptions.Json with a "
        + "source-generated JsonSerializerContext instead.";

    private static JsonSerializerOptions? _options;

    /// <summary>
    /// Web defaults (camelCase, case-insensitive reads), with every <see cref="Optional{T}"/> property left out of the
    /// JSON when it holds no value. Generated records name each property with <c>[JsonPropertyName]</c> and each enum
    /// value with <c>[JsonStringEnumMemberName]</c>, so nothing depends on the naming policy.
    /// </summary>
    /// <remarks>
    /// It reads types by reflection. A trimmed or native AOT app, where reflection is off by default, passes
    /// <see cref="TraxClientOptions.Json"/> with a source-generated context instead.
    /// </remarks>
    public static JsonSerializerOptions Options
    {
        [RequiresUnreferencedCode(ByReflection)]
        [RequiresDynamicCode(ByReflection)]
        get => _options ??= Create(new DefaultJsonTypeInfoResolver());
    }

    // The web defaults over a resolver, unset Optionals left out.
    private static JsonSerializerOptions Create(IJsonTypeInfoResolver resolver)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = resolver.WithAddedModifier(LeaveOutUnset),
        };
        options.MakeReadOnly();
        return options;
    }

    // What a client reads and writes with: a copy of the caller's options that leaves unset Optionals out too,
    // whatever resolver they read types with, or Options when there are none. Reflection is reached only when the
    // app leaves it on; with it off (a trimmed or AOT app's default) options without a resolver are refused here,
    // before anything is sent.
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "Reached only while JsonSerializer.IsReflectionEnabledByDefault is on."
    )]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050",
        Justification = "Reached only while JsonSerializer.IsReflectionEnabledByDefault is on."
    )]
    internal static JsonSerializerOptions LeavingOutUnset(JsonSerializerOptions? options)
    {
        if (options?.TypeInfoResolver is null && !JsonSerializer.IsReflectionEnabledByDefault)
            throw new ArgumentException(
                "Reflection-based serialization is off in this app: pass TraxClientOptions.Json with a "
                    + "source-generated JsonSerializerContext as its TypeInfoResolver.",
                nameof(options)
            );
        if (options is null || ReferenceEquals(options, _options))
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
