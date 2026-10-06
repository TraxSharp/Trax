using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Trax.Effect.Utils;

/// <summary>
/// Serializer options for writing Trax's models to a log: a model's <c>ToString()</c>, the JSON
/// effect's log line and the junction logger's output.
/// </summary>
/// <remarks>
/// <para>
/// Trax keeps a run's input unmasked in the places it runs from: <c>Manifest.Properties</c>,
/// <c>WorkQueue.Input</c> and <c>BackgroundJob.Input</c>. <see cref="Attributes.TraxSensitiveAttribute"/>
/// cannot mask those, because they are JSON strings, so a log serialization writes each as
/// <c>{"_omitted": true}</c>. A metadata row's navigations (<c>Manifest</c>, <c>Parent</c>,
/// <c>Children</c>, <c>Logs</c>) are left out altogether: they reach those copies, and a log
/// line is about the run, not the graph loaded around it.
/// </para>
/// <para>
/// The columns themselves are unchanged, and so is every other serialization of the models: the
/// dashboard and requeue read the columns, not these options.
/// </para>
/// </remarks>
public static class TraxLogSerialization
{
    /// <summary>
    /// The property of the object that stands in for an omitted run-from copy.
    /// </summary>
    public const string OmittedProperty = "_omitted";

    private static readonly ConditionalWeakTable<
        JsonSerializerOptions,
        JsonSerializerOptions
    > Derived = new();

    // (declaring type, member) pairs holding a run's input as the real, unmasked JSON.
    private static readonly HashSet<(Type, string)> RunFromCopies =
    [
        (typeof(Models.Manifest.Manifest), nameof(Models.Manifest.Manifest.Properties)),
        (typeof(Models.WorkQueue.WorkQueue), nameof(Models.WorkQueue.WorkQueue.Input)),
        (
            typeof(Models.BackgroundJob.BackgroundJob),
            nameof(Models.BackgroundJob.BackgroundJob.Input)
        ),
    ];

    private static readonly HashSet<string> MetadataNavigations =
    [
        nameof(Models.Metadata.Metadata.Manifest),
        nameof(Models.Metadata.Metadata.Parent),
        nameof(Models.Metadata.Metadata.Children),
        nameof(Models.Metadata.Metadata.Logs),
    ];

    /// <summary>
    /// Returns options that serialize exactly as <paramref name="options"/> do, except that
    /// <see cref="Attributes.TraxSensitiveAttribute"/> members are masked (as
    /// <see cref="TraxRedaction.WithRedaction"/> does), the run-from copies of an input are written
    /// as <c>{"_omitted": true}</c>, and a metadata row's navigations are left out.
    /// </summary>
    /// <remarks>
    /// The same instance is returned for the same <paramref name="options"/>. The returned options
    /// are for writing only.
    /// </remarks>
    public static JsonSerializerOptions ForLogging(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Derived.GetValue(options, Derive);
    }

    private static JsonSerializerOptions Derive(JsonSerializerOptions options)
    {
        var redacted = TraxRedaction.WithRedaction(options);
        var resolver = redacted.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver();
        var derived = new JsonSerializerOptions(redacted)
        {
            TypeInfoResolver = resolver.WithAddedModifier(OmitRunFromCopies),
        };
        derived.MakeReadOnly();
        return derived;
    }

    private static void OmitRunFromCopies(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        var isMetadata = typeof(Models.Metadata.Metadata).IsAssignableFrom(typeInfo.Type);
        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            var property = typeInfo.Properties[i];
            if (property.AttributeProvider is not MemberInfo member)
                continue;

            if (isMetadata && MetadataNavigations.Contains(member.Name))
                typeInfo.Properties.RemoveAt(i);
            else if (IsRunFromCopy(member))
                property.CustomConverter = (JsonConverter)
                    Activator.CreateInstance(
                        typeof(OmittingConverter<>).MakeGenericType(property.PropertyType)
                    )!;
        }
    }

    private static bool IsRunFromCopy(MemberInfo member)
    {
        for (var type = member.DeclaringType; type is not null; type = type.BaseType)
            if (RunFromCopies.Contains((type, member.Name)))
                return true;
        return false;
    }

    private sealed class OmittingConverter<T> : JsonConverter<T>
    {
        public override T Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) =>
            throw new JsonException(
                "A run-from copy was omitted from this log serialization and cannot be read back."
            );

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean(OmittedProperty, true);
            writer.WriteEndObject();
        }
    }
}
