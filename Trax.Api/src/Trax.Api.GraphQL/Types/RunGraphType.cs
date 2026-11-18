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
        descriptor
            .Field(g => g.ChainChanged)
            .Description(
                "True when the run recorded its chain's hash when it started and it differs from "
                    + "hash: the chain has changed since the run, and the nodes it is drawn on may "
                    + "not be the ones it ran. False when the run recorded none or there is no graph."
            );
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
