using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.StateMachine;

/// <summary>
/// A neutral checkout wizard: <c>Cart → Review → Paid</c>, authored declaratively, so its behaviour is data the IR
/// exporter writes out: each state's context record and rule, and each event's rule, input, guard and reduction. A
/// client starts a checkout with the declared start, which mints its id on the server, fills the cart with
/// <c>AddItem</c>, and moves it with <c>Next</c>, <c>Back</c> and <c>Reset</c>. <c>Paid</c> is committed and
/// <c>Pay</c> runs <see cref="ICharge"/> exactly once: <c>Pay</c> is effect-bound, so only a send fires it, after the
/// charge ran, with the receipt the charge returned.
///
/// <para><b>Version 2</b> adds a denormalised <c>total</c> (cents) to the context, so it is
/// <c>{ items: string[], receipt: string | null, total: number }</c>. Every state requires <c>total</c>, which makes
/// the forward migration load-bearing: a stored v1 draft (no <c>total</c>) would fail v2 validation, so
/// <see cref="Configure"/> registers a <c>MigrateFrom(1)</c> that backfills it from the item count on rehydrate.
/// That is the schema-evolution path a real persisted flow needs.</para>
///
/// <para><b>The server owns the total.</b> <c>total</c> is the amount the charge takes, so no client writes it:
/// <c>AddItem</c>'s reduction appends the item and prices the cart on the server, and every state requires
/// <c>total == items.Count * 999</c>, so a stored draft whose total disagrees with its items cannot be read. The rule
/// vocabulary has no arithmetic, so both are custom: a named reduction and a named rule, each with one C# handler,
/// which the IR carries by name.</para>
/// </summary>
[TraxMachine]
public sealed class CheckoutMachine : Machine<CheckoutState, CheckoutTrigger>
{
    // Demo unit price in cents; the real total would come from a catalog. Kept integer so the canonical wire
    // stays exact and readable.
    public const int UnitPriceCents = 999;

    /// <summary>The custom rule every state requires: the total is the server's price for the items.</summary>
    public const string TotalMatchesItems = "checkout:total-matches-items";

    /// <summary>The custom reduction of <see cref="CheckoutTrigger.AddItem"/>: append the item and price the cart.</summary>
    public const string AddItemToCart = "checkout:add-item";

    /// <summary>The amount to charge for a checkout snapshot, in cents.</summary>
    public static long AmountCents(Snapshot snapshot) =>
        long.Parse(snapshot.Context["total"]!.ToJsonString(), CultureInfo.InvariantCulture);

    private static readonly Rule HasItems = Field((CheckoutContext c) => c.Items)
        .CountGreaterThan(0);

    private static readonly Rule PricedByTheServer = new Rule.Custom(TotalMatchesItems);

    private static int ItemsCount(JsonObject ctx) => ctx["items"] is JsonArray a ? a.Count : 0;

    // The total must be the server's price for the items, not whatever was stored.
    private static bool TotalIsThePriceOfTheItems(JsonObject ctx) =>
        ctx["total"] is JsonValue total
        && total.GetValueKind() == JsonValueKind.Number
        && string.Equals(
            total.ToJsonString(),
            ((long)ItemsCount(ctx) * UnitPriceCents).ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal
        );

    // AddItem's guard has already checked the input names an item.
    private static JsonObject AddToCart(JsonObject ctx, JsonNode? input)
    {
        var next = (JsonObject)ctx.DeepClone();
        var items = next["items"] as JsonArray ?? [];
        items.Add(input!["item"]!.GetValue<string>());
        next["items"] = items;
        next["total"] = (long)items.Count * UnitPriceCents;
        return next;
    }

    private static JsonObject Fresh() =>
        new()
        {
            ["items"] = new JsonArray(),
            ["receipt"] = null,
            ["total"] = 0,
        };

    protected override void Configure(IMachineBuilder<CheckoutState, CheckoutTrigger> m)
    {
        m.Id("checkout")
            .Version(2)
            .StartsAt(CheckoutState.Cart, Fresh)
            // v1 -> v2: backfill total from the item count so a pre-total draft stays valid under v2.
            .MigrateFrom(
                1,
                (state, ctx) =>
                {
                    var next = (JsonObject)ctx.DeepClone();
                    next["total"] = ItemsCount(ctx) * UnitPriceCents;
                    return new MigrationResult(state, next);
                }
            )
            // A checkout starts as an empty cart, and only its owner reads it.
            .Start<EmptyStart>(s => s.Reduce(Keep()).AnyAuthenticated())
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly())
            .CustomGuard(TotalMatchesItems, (ctx, _) => TotalIsThePriceOfTheItems(ctx))
            .CustomReducer(AddItemToCart, AddToCart);

        // Any signed-in user may send every event of their own checkout.
        m.In(CheckoutState.Cart)
            .Context<CheckoutContext>()
            .Requires(All(Field((CheckoutContext c) => c.Receipt).Absent(), PricedByTheServer))
            .On(CheckoutTrigger.AddItem)
            .AnyAuthenticated()
            .WithInput<ItemInput>()
            .When(Input((ItemInput i) => i.Item).NonEmpty())
            .Because("Name the item to add.")
            .Reduce(new Reduction.Custom(AddItemToCart))
            .To(CheckoutState.Cart)
            .On(CheckoutTrigger.Next)
            .AnyAuthenticated()
            .When(HasItems)
            .Because("Add an item before reviewing.")
            .To(CheckoutState.Review);

        m.In(CheckoutState.Review)
            .Context<CheckoutContext>()
            .Requires(
                All(HasItems, Field((CheckoutContext c) => c.Receipt).Absent(), PricedByTheServer)
            )
            .On(CheckoutTrigger.Back)
            .AnyAuthenticated()
            .To(CheckoutState.Cart)
            .On(CheckoutTrigger.Pay)
            .AnyAuthenticated()
            .WithInput<PayInput>()
            .When(All(HasItems, Input((PayInput i) => i.Receipt).NonEmpty()))
            .Because("Checkout needs items and a receipt to be paid.")
            .RunsOnce<ICharge>("checkout:charge")
            .Reduce(Set((PaidContext c) => c.Receipt).FromInput((PayInput i) => i.Receipt))
            .To(CheckoutState.Paid);

        m.In(CheckoutState.Paid)
            .Context<PaidContext>()
            .Committed()
            .Requires(
                All(
                    Field((PaidContext c) => c.Items).CountGreaterThan(0),
                    Field((PaidContext c) => c.Receipt).NonEmpty(),
                    PricedByTheServer
                )
            )
            .On(CheckoutTrigger.Reset)
            .AnyAuthenticated()
            .Reduce(Reset())
            .To(CheckoutState.Cart);
    }
}
