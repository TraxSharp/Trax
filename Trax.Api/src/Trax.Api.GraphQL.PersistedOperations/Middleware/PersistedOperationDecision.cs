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
