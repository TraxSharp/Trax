namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// Allow inline queries carrying one of these operation names to bypass enforcement.
    /// Case-sensitive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a convenience for trusted networks, not a security control.</b> The name
    /// matched is the <c>operationName</c> the caller puts in the request body, and nothing
    /// ties it to what the document does. <c>AllowOperations("HealthProbe")</c> admits any
    /// inline document containing an operation called <c>HealthProbe</c>, whatever that
    /// operation selects. An unnamed request is matched on its <c>documentId</c> or <c>id</c>,
    /// which the caller also supplies.
    /// </para>
    /// <para>
    /// Enforcement shapes which documents run; per-type <c>[TraxAuthorize]</c> still decides who
    /// may run what, and the allowlist does not weaken that. To admit a known document from an
    /// untrusted client, persist it and have the client send its id.
    /// </para>
    /// </remarks>
    public PersistedOperationsBuilder AllowOperations(params string[] operationNames)
    {
        ArgumentNullException.ThrowIfNull(operationNames);
        foreach (var name in operationNames)
            _allowedOperationNames.Add(name);
        return this;
    }

    /// <summary>
    /// Allow inline queries whose operation name (or, for unnamed operations, document id)
    /// matches the predicate to bypass enforcement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a convenience for trusted networks, not a security control.</b> The predicate
    /// sees the <c>operationName</c> (or <c>documentId</c>) the caller puts in the request body,
    /// never the document itself. <c>id =&gt; id.StartsWith("dev_")</c> admits any inline
    /// document whose caller names its operation with a <c>dev_</c> prefix, so a predicate is
    /// only as narrow as the network in front of the endpoint. It is not a way to admit a
    /// second client's catalogue: persist that catalogue instead.
    /// </para>
    /// <para>
    /// Enforcement shapes which documents run; per-type <c>[TraxAuthorize]</c> still decides who
    /// may run what, and the allowlist does not weaken that.
    /// </para>
    /// </remarks>
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
