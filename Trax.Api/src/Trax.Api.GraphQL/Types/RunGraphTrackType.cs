using HotChocolate.Types;
using Trax.Api.DTOs;

namespace Trax.Api.GraphQL.Types;

/// <summary>The GraphQL shape of <see cref="RunGraphTrack"/>. Fields are bound explicitly.</summary>
internal sealed class RunGraphTrackType : ObjectType<RunGraphTrack>
{
    /// <inheritdoc/>
    protected override void Configure(IObjectTypeDescriptor<RunGraphTrack> descriptor)
    {
        descriptor.Name("RunGraphTrack");
        descriptor.BindFieldsExplicitly();

        descriptor.Field(t => t.Name);
        descriptor.Field(t => t.Description);
        descriptor.Field(t => t.IsFallback);
        descriptor
            .Field(t => t.Taken)
            .Description(
                "True when the run took this track. Every branch of a PARALLEL step is taken once "
                    + "the step has started."
            );
        descriptor.Field(t => t.Nodes).Type<NonNullType<ListType<NonNullType<RunGraphNodeType>>>>();
    }
}
