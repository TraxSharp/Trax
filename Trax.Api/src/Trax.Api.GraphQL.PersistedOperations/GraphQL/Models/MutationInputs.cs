namespace Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;

/// <summary>Input for the <c>deactivatePersistedOperation</c> mutation.</summary>
public sealed record DeactivatePersistedOperationInput(
    string Id,
    string Reason,
    string? TenantKey = null
);

/// <summary>Result of <c>deactivatePersistedOperation</c>.</summary>
public sealed record DeactivatePersistedOperationPayload(
    PersistedOperationDto? Operation,
    IReadOnlyList<PersistedOperationError> Errors
)
{
    /// <summary>True when the change succeeded.</summary>
    public bool Success => Operation is not null && Errors.Count == 0;
}

/// <summary>Input for the <c>restorePersistedOperation</c> mutation.</summary>
public sealed record RestorePersistedOperationInput(string Id, string? TenantKey = null);

/// <summary>Result of <c>restorePersistedOperation</c>.</summary>
public sealed record RestorePersistedOperationPayload(
    PersistedOperationDto? Operation,
    IReadOnlyList<PersistedOperationError> Errors
)
{
    /// <summary>True when the change succeeded.</summary>
    public bool Success => Operation is not null && Errors.Count == 0;
}

/// <summary>Filter for the <c>persistedOperations</c> query.</summary>
public sealed record PersistedOperationFilter(
    bool? IsActive = null,
    string? TenantKey = null,
    string? IdStartsWith = null
);

/// <summary>Paged result for the <c>persistedOperations</c> query.</summary>
public sealed record PersistedOperationsPage(
    IReadOnlyList<PersistedOperationDto> Items,
    int TotalCount
);
