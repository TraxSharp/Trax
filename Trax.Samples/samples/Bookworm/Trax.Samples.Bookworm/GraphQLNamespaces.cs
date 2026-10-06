namespace Trax.Samples.Bookworm;

/// <summary>
/// GraphQL namespace constants for Bookworm trains. Each train's <c>Namespace</c> matches its parent
/// trains folder (enforced by a meta-test) and is taken from a constant, never a string literal.
/// </summary>
public static class GraphQLNamespaces
{
    public const string Catalog = "catalog";
    public const string Lending = "lending";
}
