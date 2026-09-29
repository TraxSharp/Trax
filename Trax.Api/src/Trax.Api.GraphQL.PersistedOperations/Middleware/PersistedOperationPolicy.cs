using HotChocolate.Language;
using Trax.Api.GraphQL.PersistedOperations.Configuration;

namespace Trax.Api.GraphQL.PersistedOperations.Middleware;

/// <summary>
/// What persisted-operation enforcement does with a document that did not come from the store.
/// </summary>
internal enum PersistedOperationDecision
{
    /// <summary>Inside a carve-out: execute it.</summary>
    PassThrough,

    /// <summary>Enforcement is off: execute it, and log it when shadow logging is on.</summary>
    Log,

    /// <summary>Refuse it with <c>PERSISTED_OPERATION_REQUIRED</c>.</summary>
    Reject,
}

/// <summary>
/// The persisted-operation decision for a document the caller sent inline, independent of how it
/// arrived. The execution-pipeline middleware asks it once per request, after the document is
/// parsed, so every transport is judged the same way.
/// </summary>
internal sealed class PersistedOperationPolicy(
    PersistedOperationsOptions options,
    AllowlistMatcher allowlist
)
{
    public PersistedOperationsOptions Options => options;

    /// <summary>
    /// Decides for an inline document. <paramref name="document"/> is the parsed document; null
    /// means it did not parse, which is inside no carve-out.
    /// </summary>
    public PersistedOperationDecision Decide(
        string? operationName,
        string? documentId,
        DocumentNode? document
    )
    {
        if (allowlist.IsAllowed(operationName, documentId))
            return PersistedOperationDecision.PassThrough;

        // Both carve-outs ask what the document selects, which its text cannot answer, and
        // neither may be decided by a cheaper check that runs first.
        if (ManagementOperationDetector.IsManagementOperation(document))
            return PersistedOperationDecision.PassThrough;

        if (options.AllowIntrospection && IntrospectionDetector.IsPureIntrospection(document))
            return PersistedOperationDecision.PassThrough;

        return options.RequirePersisted
            ? PersistedOperationDecision.Reject
            : PersistedOperationDecision.Log;
    }
}
