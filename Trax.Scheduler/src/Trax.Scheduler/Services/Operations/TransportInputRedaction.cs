using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Masks the <c>[TraxSensitive]</c> members of a train input that Trax keeps unmasked because it
/// runs from it: a work queue entry's input and a manifest's properties. The runner needs the
/// real values, so the stored copy keeps them; a read shows the copy the way an execution's
/// recorded input is shown, with each sensitive member written as <c>{"_redacted": true}</c>.
/// The dashboard and the GraphQL API mask these copies with this one implementation
/// (central <c>docs/0022</c>), so a value is masked identically on both surfaces;
/// <see cref="IOperationsService.GetWorkQueueEntryDetailAsync"/> applies it to an entry's input.
/// </summary>
/// <remarks>
/// The copy is read back as its input type with the options the job dispatcher reads it with,
/// then written with <see cref="TraxRedaction.WithRedaction"/>. When that cannot be done (the
/// type is not the input of a train registered on this host, or the JSON does not read or write
/// as it, or the type's own code throws while it is read), nothing proves the copy holds no
/// sensitive member, so the whole value is masked.
/// <para>
/// A member whose declared type does not say what it holds (<see cref="object"/>,
/// <see cref="JsonElement"/>, <see cref="JsonNode"/>, <see cref="JsonDocument"/>, or a collection
/// or dictionary of them) is masked too. The stored copy was written from the runtime value, so
/// it can hold a <c>[TraxSensitive]</c> member of a type the declared one does not name, and
/// reading it back as the declared type gives JSON with nothing to mark it. The masking runs per
/// member, so the members whose types are known still read as stored.
/// </para>
/// </remarks>
public static class TransportInputRedaction
{
    /// <summary>What a value that could not be read as its type is shown as.</summary>
    internal static readonly string FullyMasked = $$"""{"{{TraxRedaction.MarkerProperty}}":true}""";

    /// <summary>
    /// The dispatcher's options with every <c>[TraxSensitive]</c> member and every open-ended
    /// member written as the mask.
    /// </summary>
    internal static readonly JsonSerializerOptions WriteOptions = MaskingOpenEndedMembers(
        TraxRedaction.WithRedaction(TraxJsonSerializationOptions.ManifestProperties)
    );

    /// <summary>
    /// The stored JSON with its sensitive members masked, or <c>null</c> when there is none.
    /// </summary>
    /// <param name="discovery">The trains registered on this host; only their inputs are read.</param>
    /// <param name="json">The stored copy, as the work queue or the manifest holds it.</param>
    /// <param name="inputTypeName">The stored type name: a FullName or an assembly-qualified name.</param>
    public static string? Redact(
        ITrainDiscoveryService discovery,
        string? json,
        string? inputTypeName
    )
    {
        if (json is null)
            return null;

        var inputType = FindInputType(discovery, inputTypeName);
        if (inputType is null)
            return FullyMasked;

        try
        {
            var options = TraxJsonSerializationOptions.ManifestProperties;
            var value = JsonSerializer.Deserialize(json, inputType, options);
            return value is null
                ? FullyMasked
                : JsonSerializer.Serialize(value, inputType, WriteOptions);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Any failure, not only a JSON one: an input type's constructor or setter can throw
            // anything, and a dashboard page renders from this.
            return FullyMasked;
        }
    }

    private static JsonSerializerOptions MaskingOpenEndedMembers(JsonSerializerOptions redacting)
    {
        var options = new JsonSerializerOptions(redacting)
        {
            TypeInfoResolver = JsonTypeInfoResolver
                .Combine([.. redacting.TypeInfoResolverChain])
                .WithAddedModifier(MaskOpenEndedMembers),
        };
        options.MakeReadOnly();
        return options;
    }

    private static void MaskOpenEndedMembers(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        foreach (var property in typeInfo.Properties)
            if (IsOpenEnded(property.PropertyType, depth: 0))
                property.CustomConverter =
                    Activator.CreateInstance(
                        typeof(MaskConverter<>).MakeGenericType(property.PropertyType)
                    ) as JsonConverter;
    }

    /// <summary>
    /// Whether a value of <paramref name="type"/> can hold members its type does not declare:
    /// <see cref="object"/>, a JSON DOM type, a non-generic collection, or a collection or
    /// dictionary whose elements are one of those.
    /// </summary>
    internal static bool IsOpenEnded(Type type, int depth)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (
            type == typeof(object)
            || type == typeof(JsonElement)
            || type == typeof(JsonDocument)
            || typeof(JsonNode).IsAssignableFrom(type)
        )
            return true;

        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
            return false;

        var elementTypes = (
            type.IsInterface ? type.GetInterfaces().Append(type) : type.GetInterfaces()
        )
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            .Select(i => ElementValueType(i.GetGenericArguments()[0]))
            .ToList();

        // A collection that names no element type reads its elements back as JSON.
        if (elementTypes.Count == 0)
            return true;

        return depth < MaxElementDepth && elementTypes.Any(e => IsOpenEnded(e, depth + 1));
    }

    /// <summary>How many collections deep an element type is looked through.</summary>
    private const int MaxElementDepth = 8;

    /// <summary>A dictionary's entries are judged by their value type.</summary>
    private static Type ElementValueType(Type element) =>
        element.IsGenericType && element.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
            ? element.GetGenericArguments()[1]
            : element;

    /// <summary>Writes the mask whatever the value is. Only ever used to write.</summary>
    private sealed class MaskConverter<T> : JsonConverter<T>
    {
        public override T Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => throw new NotSupportedException("A masked copy is never read back.");

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteBoolean(TraxRedaction.MarkerProperty, true);
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// The registered train input type a stored type name names: its FullName, or an assembly
    /// qualified name whose assembly is that type's. Only registered inputs are considered, as the
    /// job dispatcher considers them, so a stored name never loads an arbitrary type.
    /// </summary>
    internal static Type? FindInputType(ITrainDiscoveryService discovery, string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;

        foreach (var registration in discovery.DiscoverTrains())
            if (Names(typeName, registration.InputType))
                return registration.InputType;

        return null;
    }

    private static bool Names(string typeName, Type type)
    {
        var fullName = type.FullName;
        if (fullName is null || !typeName.StartsWith(fullName, StringComparison.Ordinal))
            return false;

        if (typeName.Length == fullName.Length)
            return true;

        if (typeName[fullName.Length] != ',')
            return false;

        var rest = typeName.AsSpan(fullName.Length + 1).TrimStart();
        var comma = rest.IndexOf(',');
        var assemblyName = (comma < 0 ? rest : rest[..comma]).Trim();

        return assemblyName.Equals(type.Assembly.GetName().Name, StringComparison.Ordinal);
    }
}
