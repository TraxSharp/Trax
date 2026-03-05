namespace Trax.Api.GraphQL.Mutations;

/// <summary>
/// Root mutation type that groups mutations into <c>dispatch</c> and <c>operations</c> namespaces.
/// </summary>
public class RootMutation
{
    public DispatchMutations Dispatch() => new();

    public OperationsMutations Operations() => new();
}
