namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Root query type that groups queries into <c>discover</c> and <c>operations</c> namespaces.
/// </summary>
public class RootQuery
{
    public DiscoverQueries Discover() => new();

    public OperationsQueries Operations() => new();
}
