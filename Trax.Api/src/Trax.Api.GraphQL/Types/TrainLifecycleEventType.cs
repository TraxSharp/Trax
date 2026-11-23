using System.Text.Json;
using HotChocolate.Types;
using Trax.Api.DTOs;

namespace Trax.Api.GraphQL.Types;

/// <summary>
/// Customizes the GraphQL representation of <see cref="TrainLifecycleEvent"/>.
/// The raw <c>Output</c> string is hidden; a resolver-based <c>output</c> field
/// lazily parses it into a JSON scalar only when the client selects it.
/// </summary>
internal sealed class TrainLifecycleEventType : ObjectType<TrainLifecycleEvent>
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
