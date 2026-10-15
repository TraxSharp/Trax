using HotChocolate.Types;
using Trax.Api.DTOs;

namespace Trax.Api.GraphQL.Types;

/// <summary>
/// The GraphQL shape of <see cref="RunGraph"/>, returned by <c>operations.runGraph</c>. Fields are
/// bound explicitly.
/// </summary>
internal sealed class RunGraphType : ObjectType<RunGraph>
{
    /// <inheritdoc/>
    protected override void Configure(IObjectTypeDescriptor<RunGraph> descriptor)
    {
        descriptor.Name("RunGraph");
        descriptor.BindFieldsExplicitly();

        descriptor.Field(g => g.MetadataId);
        descriptor.Field(g => g.Train).Description("The run's train, by its canonical name.");
        descriptor
            .Field(g => g.HasGraph)
            .Description(
                "False when the host has no graph for the train (not registered here, or its "
                    + "chain cannot be read outside a request): nodes is empty and every step "
                    + "is unmatched."
            );
        descriptor.Field(g => g.Hash);
        descriptor.Field(g => g.Nodes).Type<NonNullType<ListType<NonNullType<RunGraphNodeType>>>>();
        descriptor
            .Field(g => g.AllNodes)
            .Type<NonNullType<ListType<NonNullType<RunGraphNodeType>>>>()
            .Description(
                "Every node of the graph at any depth, in one list: depth first, each node before "
                    + "the nodes on its tracks, each track's nodes in declared order. Each says "
                    + "where it sits (parentId, track, depth), so a client can rebuild the tree "
                    + "without following nested tracks, which a query cannot follow past the "
                    + "server's field-cycle limit. Select only the tracks' own fields here (name, "
                    + "taken, ...), not their nodes."
            );
        descriptor
            .Field(g => g.UnmatchedSteps)
            .Type<NonNullType<ListType<NonNullType<JunctionStepGraphType>>>>()
            .Description(
                "Steps that match no node of the current graph: recorded before node ids were, "
                    + "on a withheld track, or for a chain that has changed since the run."
            );
        descriptor
            .Field(g => g.MoreSteps)
            .Description(
                "True when the run recorded more steps than one read matches (500), so a later "
                    + "node can show as not reached when it ran."
            );
        descriptor
            .Field(g => g.CanResume)
            .Description(
                "True when resumeExecution without from can resume the run after its latest "
                    + "checkpoint: it failed or was cancelled, no state machine's step started it, "
                    + "and the resume check over the declared chain allows it. The mutation can "
                    + "still refuse the run for its saved input or a resume already queued."
            );
    }
}

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
