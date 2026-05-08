namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// Allow these operation names to bypass enforcement. Case-sensitive.
    /// </summary>
    public PersistedOperationsBuilder AllowOperations(params string[] operationNames)
    {
        ArgumentNullException.ThrowIfNull(operationNames);
        foreach (var name in operationNames)
            _allowedOperationNames.Add(name);
        return this;
    }

    /// <summary>
    /// Allow operations whose name (or, for unnamed operations, document id)
    /// matches the predicate. Useful for dev-only carve-outs like
    /// <c>id =&gt; id.StartsWith("dev_")</c>.
    /// </summary>
    public PersistedOperationsBuilder AllowOperationsMatching(Func<string, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _allowOperationPredicates.Add(predicate);
        return this;
    }

    /// <summary>
    /// Disable the automatic introspection bypass. By default, introspection
    /// requests pass through enforcement so playgrounds, codegen, and
    /// schema-drift tools work without listing them in the allowlist. Call
    /// this only when you want strict prod with no introspection at all.
    /// </summary>
    public PersistedOperationsBuilder DisableIntrospection()
    {
        _allowIntrospection = false;
        return this;
    }
}
