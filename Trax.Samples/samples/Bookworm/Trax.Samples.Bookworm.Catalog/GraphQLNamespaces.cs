namespace Trax.Samples.Bookworm.Catalog;

/// <summary>
/// GraphQL namespace constants for catalog query models. Using a constant (never a string literal)
/// keeps the namespace in lockstep with the folder/domain and lets a meta-test verify it.
/// </summary>
public static class GraphQLNamespaces
{
    public const string Catalog = "catalog";
}
