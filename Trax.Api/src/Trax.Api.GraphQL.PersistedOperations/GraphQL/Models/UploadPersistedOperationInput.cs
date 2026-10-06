using System.ComponentModel;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;

/// <summary>Input for the <c>uploadPersistedOperation</c> mutation.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record UploadPersistedOperationInput(
    string Id,
    string Document,
    string? Description = null,
    bool BypassShapeDiff = false,
    int Version = 0,
    string? TenantKey = null
);
