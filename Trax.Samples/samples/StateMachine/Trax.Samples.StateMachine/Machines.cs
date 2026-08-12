using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Samples.StateMachine;

// Two worked-example machines, authored with the fluent API. A host discovers them with one line
// (AddTraxStateMachines) and drives them through the four generic `stateMachine` GraphQL mutations. The
// turnstile is the pure-structure proof (no effect); the checkout is the effectful proof (a committed state
// and one irreversible charge, fired exactly once).

public enum TurnstileState
{
    Locked,
    Unlocked,
}

public enum TurnstileTrigger
{
    Coin,
    Push,
}

/// <summary>A turnstile: <c>Locked ⇄ Unlocked</c>. No effect, no committed state, the simplest machine.</summary>
public sealed class TurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    private static readonly HashSet<string> Accepted = new(StringComparer.Ordinal)
    {
        "quarter",
        "dollar",
    };

    private static string? Coin(JsonNode? input) =>
        input is JsonObject o && o["coin"]?.GetValueKind() == JsonValueKind.String
            ? o["coin"]!.GetValue<string>()
            : null;

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        m.Id("turnstile").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());

        m.In(TurnstileState.Locked)
            .Holds(ctx => ctx.Count == 0 ? null : "Locked carries no context.")
            .On(TurnstileTrigger.Coin)
            .When((_, input) => Accepted.Contains(Coin(input) ?? string.Empty))
            .Because("Only a quarter or a dollar is accepted.")
            .Reduce((_, input) => new JsonObject { ["paidWith"] = Coin(input) })
            .To(TurnstileState.Unlocked);

        m.In(TurnstileState.Unlocked)
            .Holds(ctx =>
                ctx["paidWith"]?.GetValueKind() == JsonValueKind.String
                && ctx["paidWith"]!.GetValue<string>().Length > 0
                    ? null
                    : "Unlocked requires a non-empty paidWith."
            )
            .On(TurnstileTrigger.Push)
            .Reduce((_, _) => new JsonObject())
            .To(TurnstileState.Locked);
    }
}

public enum CheckoutState
{
    Cart,
    Review,
    Paid,
}

public enum CheckoutTrigger
{
    Next,
    Back,
    Pay,
    Reset,
}

/// <summary>The irreversible effect on <c>Pay</c>: charge the order. Bound inline via <c>RunsOnce&lt;ICharge&gt;</c>.</summary>
public interface ICharge : ISnapshotEffect { }

/// <summary>A demo charge that logs and returns a receipt. A real host swaps in a payment gateway.</summary>
public sealed class LoggingCharge(ILogger<LoggingCharge> logger) : ICharge
{
    public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default)
    {
        var receipt = $"rcpt_{Guid.NewGuid():N}";
        logger.LogInformation(
            "Charged checkout {State} -> receipt {Receipt}",
            snapshot.State,
            receipt
        );
        return Task.FromResult(receipt);
    }
}

/// <summary>
/// A neutral checkout wizard: <c>Cart → Review → Paid</c>. <c>Paid</c> is committed (a soft autosave can't
/// resurrect it) and <c>Pay</c> runs <see cref="ICharge"/> exactly once, both declared inline.
///
/// <para><b>Version 2</b> adds a denormalised <c>total</c> (cents) to the context, so it is
/// <c>{ items: string[], receipt: string | null, total: number }</c>. Every state requires <c>total</c> to be
/// a number, which makes the forward migration load-bearing: a stored v1 draft (no <c>total</c>) would fail
/// v2 validation, so <see cref="Configure"/> registers a <c>MigrateFrom(1)</c> that backfills it from the
/// item count on rehydrate. That is the schema-evolution path a real persisted flow needs.</para>
/// </summary>
public sealed class CheckoutMachine : Machine<CheckoutState, CheckoutTrigger>
{
    // Demo unit price in cents; the real total would come from a catalog. Kept integer so the canonical wire
    // stays exact and readable.
    private const int UnitPriceCents = 999;

    private static int ItemsCount(JsonObject ctx) => ctx["items"] is JsonArray a ? a.Count : 0;

    private static bool ItemsIsArray(JsonObject ctx) => ctx["items"] is JsonArray;

    private static bool TotalIsNumber(JsonObject ctx) =>
        ctx["total"]?.GetValueKind() == JsonValueKind.Number;

    private static bool ReceiptEmpty(JsonObject ctx) =>
        ctx["receipt"] is null || ctx["receipt"]!.GetValueKind() == JsonValueKind.Null;

    private static bool ReceiptPresent(JsonObject ctx) =>
        ctx["receipt"]?.GetValueKind() == JsonValueKind.String
        && ctx["receipt"]!.GetValue<string>().Length > 0;

    private static string? Receipt(JsonNode? input) =>
        input is JsonObject o && o["receipt"]?.GetValueKind() == JsonValueKind.String
            ? o["receipt"]!.GetValue<string>()
            : null;

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
            );

        m.In(CheckoutState.Cart)
            .Holds(ctx =>
                ItemsIsArray(ctx) && ReceiptEmpty(ctx) && TotalIsNumber(ctx)
                    ? null
                    : "Cart: items[], no receipt, numeric total."
            )
            .On(CheckoutTrigger.Next)
            .To(CheckoutState.Review);

        m.In(CheckoutState.Review)
            .Holds(ctx =>
                ItemsCount(ctx) > 0 && ReceiptEmpty(ctx) && TotalIsNumber(ctx)
                    ? null
                    : "Review: non-empty items, no receipt, numeric total."
            )
            .On(CheckoutTrigger.Back)
            .To(CheckoutState.Cart)
            .On(CheckoutTrigger.Pay)
            .When((ctx, input) => ItemsCount(ctx) > 0 && Receipt(input) is not null)
            .Because("Checkout needs items and a receipt to be paid.")
            .RunsOnce<ICharge>("checkout:charge")
            .Reduce(
                (ctx, input) =>
                {
                    var next = (JsonObject)ctx.DeepClone();
                    next["receipt"] = Receipt(input);
                    return next;
                }
            )
            .To(CheckoutState.Paid);

        m.In(CheckoutState.Paid)
            .Committed()
            .Holds(ctx =>
                ItemsCount(ctx) > 0 && ReceiptPresent(ctx) && TotalIsNumber(ctx)
                    ? null
                    : "Paid: non-empty items, a receipt, numeric total."
            )
            .On(CheckoutTrigger.Reset)
            .Reduce((_, _) => Fresh())
            .To(CheckoutState.Cart);
    }
}
