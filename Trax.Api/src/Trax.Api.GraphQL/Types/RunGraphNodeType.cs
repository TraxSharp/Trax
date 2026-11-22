using HotChocolate.Types;
using Trax.Api.DTOs;

namespace Trax.Api.GraphQL.Types;

/// <summary>The GraphQL shape of <see cref="RunGraphNode"/>. Fields are bound explicitly.</summary>
internal sealed class RunGraphNodeType : ObjectType<RunGraphNode>
{
    /// <inheritdoc/>
    protected override void Configure(IObjectTypeDescriptor<RunGraphNode> descriptor)
    {
        descriptor.Name("RunGraphNode");
        descriptor.BindFieldsExplicitly();

        descriptor.Field(n => n.Id);
        descriptor.Field(n => n.Kind);
        descriptor.Field(n => n.Junction);
        descriptor.Field(n => n.In);
        descriptor.Field(n => n.Out);
        descriptor.Field(n => n.Opaque);
        descriptor
            .Field(n => n.State)
            .Description(
                "Where the node stands in this run. A PARALLEL step records nothing itself: it is "
                    + "FAILED when any branch failed, CANCELLED when one was stopped, IN_PROGRESS "
                    + "while any branch is still going, down the tracks its routing steps took, "
                    + "and COMPLETED once every branch has finished. A CHECKPOINT is COMPLETED once "
                    + "the run stored it. In a resumed run, a node before the point it resumed at "
                    + "is RESTORED: the run skipped it. Once the run has ended, a step whose end "
                    + "was never recorded (the host stopped mid-junction, or the end event was "
                    + "dropped) is INTERRUPTED, never IN_PROGRESS."
            );
        descriptor
            .Field(n => n.ParentId)
            .Description(
                "The id of the routing or PARALLEL step whose track this node sits on, or null "
                    + "at the chain's top level."
            );
        descriptor
            .Field(n => n.Track)
            .Description(
                "The name of the track or branch this node sits on, or null at the chain's top "
                    + "level."
            );
        ConfigureDepthAndRunFields(descriptor);
    }

    /// <summary>Binds how deep a node sits, whether it can resume, and what it ran.</summary>
    private static void ConfigureDepthAndRunFields(IObjectTypeDescriptor<RunGraphNode> descriptor)
    {
        descriptor
            .Field(n => n.Depth)
            .Description("How many tracks deep the node sits: 0 at the chain's top level.");
        descriptor
            .Field(n => n.CanResume)
            .Description(
                "True when resumeExecution(id, from) can resume the run at this node: the run "
                    + "failed or was cancelled and a checkpoint before the node lets it run on "
                    + "what that checkpoint restores."
            );
        descriptor
            .Field(n => n.Checkpointed)
            .Description(
                "True when a checkpoint the run can resume from is stored at this node. Only that "
                    + "it exists: what it holds is never returned."
            );
        descriptor.Field(n => n.Replayed);
        descriptor
            .Field(n => n.TrackTaken)
            .Description(
                "The track a routing step took, or null when it took none or it cannot be told. "
                    + "Always null for a PARALLEL step, which runs every branch."
            );
        descriptor
            .Field(n => n.Steps)
            .Type<NonNullType<ListType<NonNullType<JunctionStepGraphType>>>>();
        descriptor
            .Field(n => n.Tracks)
            .Type<NonNullType<ListType<NonNullType<RunGraphTrackType>>>>()
            .Description(
                "A routing step's tracks, or a PARALLEL step's branches, in declared order. "
                    + "Empty for any other step."
            );
    }
}
