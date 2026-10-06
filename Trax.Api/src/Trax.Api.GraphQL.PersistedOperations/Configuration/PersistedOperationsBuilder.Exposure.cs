namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// Whether to graft the <c>operations</c> namespace onto the schema. On by default, because
    /// that is where the persisted-operation management mutations live.
    /// </summary>
    /// <remarks>
    /// Turning it off keeps storage, enforcement, the cache and cross-node invalidation, and
    /// leaves the schema without <c>operations</c>. Persisted operations and a GraphQL-exposed
    /// scheduler console are separable features: a host that wants its operations enforced but
    /// manages them out of band (a migration, a deploy step, a separate admin process) should not
    /// have to publish the control plane to get enforcement.
    /// <para>
    /// The namespace is also what <c>ExposeOperationQueries()</c> and
    /// <c>ExposeOperationMutations()</c> add, so a host that calls one of those directly still
    /// gets it, and still has to answer for it.
    /// </para>
    /// </remarks>
    public PersistedOperationsBuilder ExposeOperationsNamespace(bool expose = true)
    {
        _exposeOperationsNamespace = expose;
        return this;
    }
}
