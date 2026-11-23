using System.ComponentModel;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;

/// <summary>Input for the <c>restorePersistedOperation</c> mutation.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record RestorePersistedOperationInput(string Id, string? TenantKey = null);
