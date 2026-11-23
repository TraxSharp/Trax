using System.Globalization;

namespace Trax.Samples.Board.Shop;

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
    }.ToDictionary(r => r.Key, StringComparer.Ordinal);
}
