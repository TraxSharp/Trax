namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// Reject requests with an inline <c>query</c> body. Defaults to true.
    /// Set false to operate in shadow mode (combine with
    /// <see cref="LogNonPersistedRequests"/>).
    /// </summary>
    public PersistedOperationsBuilder RequirePersisted(bool require = true)
    {
        _requirePersisted = require;
        return this;
    }

    /// <summary>
    /// When true, log every inline-query request at Information level.
    /// Enable during phased rollout so you can see what would be rejected
    /// before flipping <see cref="RequirePersisted"/> on.
    /// </summary>
    public PersistedOperationsBuilder LogNonPersistedRequests(bool log = true)
    {
        _logNonPersistedRequests = log;
        return this;
    }
}
