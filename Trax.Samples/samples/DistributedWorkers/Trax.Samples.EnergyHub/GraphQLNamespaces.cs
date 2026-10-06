namespace Trax.Samples.EnergyHub;

/// <summary>
/// GraphQL namespace constants for the energy hub API.
/// Trains sharing a namespace are grouped under the same
/// sub-field in the schema (e.g. <c>discover { solar { monitorSolarProduction } }</c>).
/// </summary>
public static class GraphQLNamespaces
{
    public const string Solar = "solar";
    public const string Battery = "battery";
    public const string Sustainability = "sustainability";
}
