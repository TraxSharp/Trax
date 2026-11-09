namespace Trax.Samples.CheckIn.Api.Auth;

/// <summary>
/// The roles the glossary's actors carry, a passenger owning bookings and a gate agent desks, and the operator's, who
/// reads the operations console.
/// </summary>
public static class CheckInRoles
{
    public const string Passenger = "passenger";
    public const string Agent = "agent";
    public const string Operator = "operator";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Passenger,
        Agent,
        Operator,
    };
}
