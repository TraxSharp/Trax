namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Root query type. The <c>operations</c> namespace is always present.
/// The <c>discover</c> namespace is added dynamically by <see cref="TypeModules.TrainTypeModule"/>
/// only when trains annotated with <c>[TraxQuery]</c> are registered.
/// </summary>
public class RootQuery
{
    public OperationsQueries Operations() => new();
}
