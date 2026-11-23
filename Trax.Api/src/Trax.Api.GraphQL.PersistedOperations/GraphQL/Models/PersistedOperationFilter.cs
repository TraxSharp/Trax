using System.ComponentModel;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;

/// <summary>Filter for the <c>persistedOperations</c> query.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record PersistedOperationFilter(
    bool? IsActive = null,
    string? TenantKey = null,
    string? IdStartsWith = null
);
