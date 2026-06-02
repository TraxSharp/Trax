namespace Trax.Samples.Api.Data;

/// <summary>
/// The single PostgreSQL schema this app's data context owns. One project : one schema : one
/// context. Referenced wherever the schema name is needed so it never appears as a string literal.
/// </summary>
public static class AppSchema
{
    public const string Name = "app";
}
