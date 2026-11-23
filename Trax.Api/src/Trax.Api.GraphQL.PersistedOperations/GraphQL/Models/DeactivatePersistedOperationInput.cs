using System.ComponentModel;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;

/// <summary>Input for the <c>deactivatePersistedOperation</c> mutation.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record DeactivatePersistedOperationInput(
    string Id,
    string Reason,
    string? TenantKey = null
);
