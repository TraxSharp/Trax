using Trax.Api.GraphQL.PersistedOperations.Configuration;

namespace Trax.Api.GraphQL.PersistedOperations.Middleware;

/// <summary>
/// Combines the configured operation-name allowlist and predicate list into
/// a single decision. Pure function, no allocations on the hot path beyond
/// the predicate evaluation.
/// </summary>
internal sealed class AllowlistMatcher
{
    private readonly PersistedOperationsOptions _options;

    public AllowlistMatcher(PersistedOperationsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// True when the operation name (or document id, when no name is given)
    /// matches an allowlist entry or any registered predicate.
    /// </summary>
    public bool IsAllowed(string? operationName, string? documentId)
    {
        var key = operationName ?? documentId;
        if (string.IsNullOrEmpty(key))
            return false;

        if (_options.AllowedOperationNames.Contains(key))
            return true;

        foreach (var predicate in _options.AllowOperationPredicates)
            if (predicate(key))
                return true;

        return false;
    }
}
