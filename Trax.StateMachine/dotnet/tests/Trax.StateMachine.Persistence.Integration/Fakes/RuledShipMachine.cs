using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// A shipment whose one effect-bound event, <c>Ship</c>, requires Operator: <c>Packing -Ship-&gt; Shipped</c>, running
/// <see cref="IOrderCharge"/> exactly once.
/// </summary>
public sealed class RuledShipMachine : Machine<ShipState, ShipTrigger>
{
    public const string Id = "ruled-ship";

    protected override void Configure(IMachineBuilder<ShipState, ShipTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(ShipState.Packing, () => new JsonObject())
            .Start<DeskStart>(s => s.Reduce(Keep()).AnyAuthenticated())
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(ShipState.Packing)
            .On(ShipTrigger.Ship)
            .Requires(DeskRoles.Operator)
            .RunsOnce<IOrderCharge>($"{Id}:ship")
            .Reduce((_, input) => new JsonObject { ["receipt"] = input?["receipt"]?.DeepClone() })
            .To(ShipState.Shipped);
    }

    public static Snapshot Packing =>
        new()
        {
            Machine = Id,
            Version = 1,
            State = nameof(ShipState.Packing),
            Context = new JsonObject(),
        };
}
