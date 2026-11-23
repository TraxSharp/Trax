using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Persistence;

namespace Trax.Samples.StateMachine;

[TraxAllowAnonymous]
[TraxQuery(Namespace = "stateMachine", Description = "List the registered state machines.")]
public class ListMachines : ServiceTrain<ListMachinesInput, ListMachinesOutput>, IListMachines
{
    protected override Task<Either<Exception, ListMachinesOutput>> Junctions() =>
        Chain<ListMachinesJunction>().Resolve();
}
