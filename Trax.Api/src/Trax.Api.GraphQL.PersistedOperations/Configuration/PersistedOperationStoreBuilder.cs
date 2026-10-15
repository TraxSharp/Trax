using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

/// <summary>
/// Configuration passed to <c>AddPersistedOperationStore(store =&gt; ...)</c>: how a change made
/// through this process reaches the GraphQL nodes that serve the operations.
/// </summary>
/// <remarks>
/// Every change the store makes empties the caches on the nodes that serve the operations, but
/// only if it reaches them. The store refuses to start until the host says how: a broker, or a
/// declaration that this process is the only one that caches them.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/persisted-operations/add-persisted-operation-store">SDK reference</seealso>
public sealed class PersistedOperationStoreBuilder
{
    // Created by AddPersistedOperationStore and handed to its callback.
    internal PersistedOperationStoreBuilder() { }

    private string? _rabbitMqConnectionString;
    private bool _singleNode;

    // Validated and accumulated as UsePersistedOperations does, so the two gates read the same.
    private readonly PersistedOperationsBuilder _management = new();

    /// <summary>The management requirements declared here, empty when none was.</summary>
    internal IReadOnlyList<TraxAuthorizeAttribute> ManagementGate => _management.ManagementGate;

    /// <summary>
    /// Declares who may manage documents through this process's surfaces, such as the dashboard's
    /// persisted-operations pages, as <c>GateManagement</c> does on <c>UsePersistedOperations</c>.
    /// Without it the dashboard shows no management pages. Code that calls the service with no
    /// person behind it (a deploy step, a migration) is not gated. In a process that also calls
    /// <c>UsePersistedOperations</c>, its gate is the one in force, and a different one declared
    /// here refuses the host at startup.
    /// </summary>
    /// <param name="policy">An ASP.NET Core authorization policy name that must be satisfied.</param>
    /// <param name="roles">A comma-separated list of roles. The caller must hold at least one.</param>
    /// <exception cref="InvalidOperationException">
    /// Neither <paramref name="policy"/> nor <paramref name="roles"/> is given, or
    /// <paramref name="roles"/> names no role.
    /// </exception>
    public PersistedOperationStoreBuilder GateManagement(
        string? policy = null,
        string? roles = null
    )
    {
        _management.GateManagement(policy, roles);
        return this;
    }

    /// <summary>
    /// Lets any authenticated person manage documents through this process's surfaces. Prefer
    /// <see cref="GateManagement"/>.
    /// </summary>
    public PersistedOperationStoreBuilder GateManagementToAuthenticatedUsers()
    {
        _management.GateManagementToAuthenticatedUsers();
        return this;
    }

    /// <summary>
    /// Broadcast every change over RabbitMQ to the GraphQL nodes that call
    /// <c>UseRabbitMqInvalidation</c> with the same broker, so each of them empties its caches.
    /// </summary>
    public PersistedOperationStoreBuilder UseRabbitMqInvalidation(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException(
                "UseRabbitMqInvalidation requires a connection string.",
                nameof(connectionString)
            );

        _rabbitMqConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Declare that no other process caches these operations: this one serves them itself (it
    /// also calls <c>UsePersistedOperations</c>), or nothing serves them while it writes.
    /// </summary>
    /// <remarks>
    /// The declaration is a claim nothing can check. A GraphQL node in another process caches what
    /// it serves, and a change made here does not reach it until that entry's maximum age.
    /// </remarks>
    public PersistedOperationStoreBuilder SingleNode()
    {
        _singleNode = true;
        return this;
    }

    /// <summary>The broker connection string, or null when the host declared a single node.</summary>
    internal string? Build()
    {
        if (_rabbitMqConnectionString is null && !_singleNode)
            throw new InvalidOperationException(
                "AddPersistedOperationStore needs to know how a change reaches the nodes that serve "
                    + "these operations. Each node caches what it serves, so a change made through this "
                    + "store is seen there only if it is broadcast. Call "
                    + "UseRabbitMqInvalidation(connectionString) with the broker the GraphQL nodes use, or "
                    + "SingleNode() when no other process caches these operations."
            );

        if (_rabbitMqConnectionString is not null && _singleNode)
            throw new InvalidOperationException(
                "SingleNode() and UseRabbitMqInvalidation(...) contradict each other. "
                    + "Keep UseRabbitMqInvalidation when GraphQL nodes in other processes serve these "
                    + "operations, or SingleNode() when none does."
            );

        return _rabbitMqConnectionString;
    }
}
