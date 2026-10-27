using System.Globalization;

namespace Trax.Samples.Board.Shop;

/// <summary>
/// The project the tickets are about: a shop's shipping rules, as data. An order ships free from
/// <see cref="FreeFrom"/> (including it when <see cref="Inclusive"/>), and pays <see cref="FlatRate"/> below it.
/// Everything an agent sends (a case, a patch, a sweep) is data the server runs against this, never code.
/// </summary>
public sealed record ShippingRules(decimal FreeFrom, bool Inclusive, decimal FlatRate)
{
    /// <summary>What an order of <paramref name="subtotal"/> pays for shipping.</summary>
    public decimal Charge(decimal subtotal) =>
        (Inclusive ? subtotal >= FreeFrom : subtotal > FreeFrom) ? 0m : FlatRate;

    /// <summary>These rules with a fixer's patch applied: the threshold and whether it is included.</summary>
    public ShippingRules With(decimal freeFrom, bool inclusive) =>
        this with
        {
            FreeFrom = freeFrom,
            Inclusive = inclusive,
        };

    public static string Money(decimal amount) =>
        "$" + amount.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>
/// A bug report the board can be given: what the shop's rules do today (<see cref="Current"/>), what the shop's
/// policy says they should do (<see cref="Policy"/>, the server's truth that every check compares with), and the
/// cases an earlier fix was checked against, which a new patch must still pass.
/// </summary>
public sealed record Report(
    string Key,
    string Title,
    string Text,
    string Area,
    ShippingRules Current,
    ShippingRules Policy,
    IReadOnlyList<decimal> EarlierCases
);

/// <summary>The reports a ticket can be filed from, by key.</summary>
public static class Reports
{
    public static readonly IReadOnlyDictionary<string, Report> All = new[]
    {
        new Report(
            "boundary",
            "Orders of exactly $50 still pay shipping",
            "Policy: orders of $50.00 or more ship free; below that, shipping is $5.99.",
            "shipping",
            Current: new ShippingRules(50m, Inclusive: false, 5.99m),
            Policy: new ShippingRules(50m, Inclusive: true, 5.99m),
            EarlierCases: [20m, 49.99m, 75m, 120m]
        ),
        new Report(
            "forty",
            "Orders of exactly $40 still pay shipping",
            "Policy: orders of $40.00 or more ship free; below that, shipping is $3.99.",
            "shipping",
            Current: new ShippingRules(40m, Inclusive: false, 3.99m),
            Policy: new ShippingRules(40m, Inclusive: true, 3.99m),
            EarlierCases: [15m, 39.99m, 60m]
        ),
        new Report(
            "threshold",
            "Free shipping starts at $75, the policy says $60",
            "Policy: orders of $60.00 or more ship free; below that, shipping is $4.99.",
            "shipping",
            Current: new ShippingRules(75m, Inclusive: true, 4.99m),
            Policy: new ShippingRules(60m, Inclusive: true, 4.99m),
            EarlierCases: [20m, 59.99m, 90m]
        ),
        new Report(
            "hundred",
            "A $100.00 order still pays shipping",
            "Policy: orders of $100.00 or more ship free; below that, shipping is $7.50.",
            "shipping",
            Current: new ShippingRules(100m, Inclusive: false, 7.50m),
            Policy: new ShippingRules(100m, Inclusive: true, 7.50m),
            EarlierCases: [25m, 100.01m, 150m]
        ),
        new Report(
            "fixed-already",
            "A $75 order was charged shipping",
            "Policy: orders of $50.00 or more ship free; below that, shipping is $5.99.",
            "shipping",
            Current: new ShippingRules(50m, Inclusive: true, 5.99m),
            Policy: new ShippingRules(50m, Inclusive: true, 5.99m),
            EarlierCases: [20m, 49.99m, 75m]
        ),
    }.ToDictionary(r => r.Key);
}
