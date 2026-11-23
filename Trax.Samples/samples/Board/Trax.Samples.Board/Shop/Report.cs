using System.Globalization;

namespace Trax.Samples.Board.Shop;

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
