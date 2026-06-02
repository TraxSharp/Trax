namespace Trax.Samples.Bookworm.Lending;

/// <summary>
/// The single PostgreSQL schema owned by the lending domain. One project : one schema : one context.
/// </summary>
public static class LendingSchema
{
    public const string Name = "lending";
}
