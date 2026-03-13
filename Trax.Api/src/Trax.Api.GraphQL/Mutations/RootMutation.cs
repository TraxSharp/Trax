namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// Root mutation type. The <c>operations</c> namespace is always present.
/// The <c>dispatch</c> namespace is added dynamically by <see cref="TypeModules.TrainTypeModule"/>
/// only when trains annotated with <c>[TraxMutation]</c> are registered.
/// </summary>
public class RootMutation
{
    public OperationsMutations Operations() => new();
}
