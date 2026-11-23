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
