using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>
/// A declarative shop with a timer: <c>Ordered</c> expires after an hour. Two events enter <c>Ordered</c>,
/// <c>Submit</c> (any authenticated caller) and <c>Reopen</c> (Clerk), so the timer leaving it carries the union of
/// their rules.
/// </summary>
[TraxMachine]
public sealed class ShopMachine : Machine<ShopState, ShopTrigger>
{
    public const string MachineId = "shop";

    /// <summary>The client events, by the field name each has under <c>machines.shop</c>.</summary>
    public static readonly IReadOnlyList<string> ClientEventFields =
    [
        "submit",
        "ship",
        "approve",
        "reopen",
        "restart",
    ];

    protected override void Configure(IMachineBuilder<ShopState, ShopTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(ShopState.Browsing, () => new JsonObject());
        m.Start<ShopStartInput>(s =>
            s.Reduce(Set((ShopContext c) => c.Item).FromInput((ShopStartInput i) => i.Item))
                .AnyAuthenticated()
        );
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());

        m.In(ShopState.Browsing)
            .Context<ShopContext>()
            .On(ShopTrigger.Submit)
            .WithInput<ShopOrderInput>()
            .AnyAuthenticated()
            .Reduce(Set((ShopContext c) => c.Quantity).FromInput((ShopOrderInput i) => i.Quantity))
            .To(ShopState.Ordered);

        m.In(ShopState.Ordered)
            .Context<ShopContext>()
            .On(ShopTrigger.Ship)
            .WithInput<ShipToInput>()
            .AnyAuthenticated()
            .Reduce(Set((ShopContext c) => c.Street).FromInput((ShipToInput i) => i.Street))
            .To(ShopState.Ordered)
            .On(ShopTrigger.Approve)
            .Requires("Approver")
            .To(ShopState.Approved)
            .After(TimeSpan.FromHours(1))
            .GoTo(ShopState.Expired);

        m.In(ShopState.Expired)
            .Context<ShopContext>()
            .On(ShopTrigger.Reopen)
            .Requires("Clerk")
            .To(ShopState.Ordered);

        m.In(ShopState.Approved)
            .Context<ShopContext>()
            .On(ShopTrigger.Restart)
            .AnyAuthenticated()
            .To(ShopState.Browsing);
    }
}
