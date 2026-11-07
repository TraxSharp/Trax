namespace Trax.Samples.Bakery.Api.Auth;

/// <summary>
/// The roles the glossary's actors carry, a customer owning orders and a baker shifts, and the operator's, who reads
/// the operations console.
/// </summary>
public static class BakeryRoles
{
    public const string Customer = "customer";
    public const string Baker = "baker";
    public const string Operator = "operator";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Customer,
        Baker,
        Operator,
    };
}
