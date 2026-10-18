using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.Services.Runs;
using Trax.Mediator.Services.ChainVerification;

namespace Trax.Api.GraphQL.Types;

/// <summary>
/// The GraphQL shape of <see cref="TrainInfo"/>, returned by <c>operations.trains</c>: its own
/// fields as the record declares them, and the train's declared chain, read only when a query
/// selects it.
/// </summary>
internal sealed class TrainInfoType : ObjectType<TrainInfo>
{
    /// <inheritdoc/>
    protected override void Configure(IObjectTypeDescriptor<TrainInfo> descriptor)
    {
        descriptor.Name("TrainInfo");

        descriptor
            .Field("chain")
            .Type<NonNullType<RunGraphType>>()
            .Resolve(ctx =>
                RunGraphs.Declared(
                    ctx.Services.GetService<ITrainChainGraphs>(),
                    ctx.Parent<TrainInfo>().FullName
                )
            )
            .Description(
                "The train's declared chain, drawn the way runGraph draws a run of it, with no "
                    + "run: the same nodes in the same order, every one NOT_REACHED, no steps, and "
                    + "metadataId 0. hasGraph is false when this host cannot read the train's chain."
            );

        descriptor
            .Field("asksDecisions")
            .Type<NonNullType<BooleanType>>()
            .Resolve(ctx =>
                RunGraphs.AsksDecisions(
                    ctx.Services.GetService<ITrainChainGraphs>(),
                    ctx.Parent<TrainInfo>().FullName
                )
            )
            .Description(
                "True when the train declares a step that asks a decider and records the "
                    + "decision (a Decide, or a Switch, Gate or Scale that asks its own question). "
                    + "Only such a train's runs have decisions a requeue can replay or ask afresh. "
                    + "False when this host cannot read the train's chain."
            );
    }
}
