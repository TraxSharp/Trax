using System.Text.Json;
using HotChocolate.Types;
using Trax.Api.DTOs;

namespace Trax.Api.GraphQL.Types;

/// <summary>
/// Customizes the GraphQL representation of <see cref="TrainLifecycleEvent"/>.
/// The raw <c>Output</c> string is hidden; a resolver-based <c>output</c> field
/// lazily parses it into a JSON scalar only when the client selects it.
/// </summary>
internal class TrainLifecycleEventType : ObjectType<TrainLifecycleEvent>
{
    /// <inheritdoc/>
    protected override void Configure(IObjectTypeDescriptor<TrainLifecycleEvent> descriptor)
    {
        descriptor.BindFieldsExplicitly();

        descriptor.Field(e => e.MetadataId);
        descriptor.Field(e => e.ExternalId);
        descriptor.Field(e => e.TrainName);
        descriptor.Field(e => e.TrainState);
        descriptor.Field(e => e.Timestamp);
        descriptor.Field(e => e.FailureJunction);
        descriptor.Field(e => e.FailureReason);
        descriptor.Field(e => e.HostName);
        descriptor.Field(e => e.HostEnvironment);
        descriptor
            .Field(e => e.Sequence)
            .Description(
                "This event's position in the subscription: 1 for the first event and one more for "
                    + "each after it. A jump of more than one means events were lost on the way to "
                    + "this subscriber; refetch what you show. The live feed is lossy under load."
            );

        descriptor
            .Field("output")
            .Type<AnyType>()
            .Resolve(ctx =>
            {
                var output = ctx.Parent<TrainLifecycleEvent>().Output;
                if (output is null)
                    return null;

                // HotChocolate's AnyType carries a JsonElement, so objects and arrays reach the
                // client as JSON rather than failing to coerce.
                using var document = JsonDocument.Parse(output);
                return document.RootElement.Clone();
            });
    }
}

/// <summary>
/// Converts a JSON string into native .NET types (dictionaries, lists, primitives).
/// Useful for custom resolvers or lifecycle event handlers that need to deserialize
/// raw JSON output strings into structured objects.
/// </summary>
internal static class JsonElementConverter
{
    /// <summary>
    /// Parses a JSON string and converts it into native .NET types
    /// (dictionaries, lists, strings, numbers, booleans, or null).
    /// </summary>
    /// <param name="json">The raw JSON string to parse</param>
    /// <returns>A native .NET object representing the JSON structure, or null</returns>
    public static object? ToObject(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ConvertElement(doc.RootElement);
    }

    /// <summary>
    /// Converts a <see cref="JsonElement"/> into a native .NET type.
    /// Objects become dictionaries, arrays become lists, and primitives
    /// are converted to their corresponding .NET types.
    /// </summary>
    /// <param name="element">The JSON element to convert</param>
    /// <returns>A native .NET object representing the element, or null</returns>
    public static object? ConvertElement(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => element
                .EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertElement(p.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => ConvertNumber(element),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };

    private static object ConvertNumber(JsonElement element)
    {
        if (element.TryGetInt64(out var l))
            return l;

        return element.GetDouble();
    }
}
