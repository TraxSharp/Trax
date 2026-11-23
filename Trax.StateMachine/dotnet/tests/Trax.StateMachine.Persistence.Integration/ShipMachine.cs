using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>An effect whose target state is not marked committed: <c>Packing -Ship-&gt; Shipped</c>.</summary>
public sealed class ShipMachine : Machine<ShipState, ShipTrigger>
{
    protected override void Configure(IMachineBuilder<ShipState, ShipTrigger> m)
    {
        m.Id("ship").Version(1).StartsAt(ShipState.Packing, () => new JsonObject());
        m.In(ShipState.Packing)
            .On(ShipTrigger.Ship)
            .RunsOnce<IOrderCharge>()
            .Reduce((_, input) => new JsonObject { ["receipt"] = input?["receipt"]?.DeepClone() })
            .To(ShipState.Shipped);
    }
}
