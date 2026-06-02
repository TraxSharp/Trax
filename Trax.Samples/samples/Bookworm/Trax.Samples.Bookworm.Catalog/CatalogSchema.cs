namespace Trax.Samples.Bookworm.Catalog;

/// <summary>
/// The single PostgreSQL schema owned by the catalog domain. One project : one schema : one
/// context. Referenced wherever the schema name is needed so it never appears as a string literal.
/// </summary>
public static class CatalogSchema
{
    public const string Name = "catalog";
}
