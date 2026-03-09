using System.Text.Json;
using HotChocolate.Types;
using Trax.Api.DTOs;

namespace Trax.Api.GraphQL.Types;

/// <summary>
/// Customizes the GraphQL representation of <see cref="TrainLifecycleEvent"/>.
/// The raw <c>Output</c> string is hidden; a resolver-based <c>output</c> field
/// lazily parses it into a JSON scalar only when the client selects it.
/// </summary>
public class TrainLifecycleEventType : ObjectType<TrainLifecycleEvent>
{
    protected override void Configure(IObjectTypeDescriptor<TrainLifecycleEvent> descriptor)
    {
        descriptor.BindFieldsExplicitly();

        descriptor.Field(e => e.MetadataId);
        descriptor.Field(e => e.ExternalId);
        descriptor.Field(e => e.TrainName);
        descriptor.Field(e => e.TrainState);
        descriptor.Field(e => e.Timestamp);
        descriptor.Field(e => e.FailureStep);
        descriptor.Field(e => e.FailureReason);

        descriptor
            .Field("output")
            .Type<AnyType>()
            .Resolve(ctx =>
            {
                var output = ctx.Parent<TrainLifecycleEvent>().Output;
                if (output is null)
                    return null;

                return JsonElementConverter.ToObject(output);
            });
    }
}

/// <summary>
/// Converts a JSON string into native .NET types (dictionaries, lists, primitives)
/// that HotChocolate's <see cref="AnyType"/> can serialize as proper GraphQL JSON.
/// </summary>
internal static class JsonElementConverter
{
    internal static object? ToObject(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ConvertElement(doc.RootElement);
    }

    internal static object? ConvertElement(JsonElement element) =>
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
