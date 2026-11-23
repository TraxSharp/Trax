using System.ComponentModel;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;

/// <summary>Result of <c>deactivatePersistedOperation</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record DeactivatePersistedOperationPayload(
    PersistedOperationDto? Operation,
    IReadOnlyList<PersistedOperationError> Errors
)
{
    /// <summary>True when the change succeeded.</summary>
    public bool Success => Operation is not null && Errors.Count == 0;
}
