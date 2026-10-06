namespace Trax.Samples.EnergyHub;

/// <summary>
/// The roles the energy hub's trains require. A mutation queues work that moves energy or money
/// (a grid trade, a battery cycle), so only an operator may dispatch one.
/// </summary>
public static class EnergyHubRoles
{
    public const string Operator = "Operator";
}
