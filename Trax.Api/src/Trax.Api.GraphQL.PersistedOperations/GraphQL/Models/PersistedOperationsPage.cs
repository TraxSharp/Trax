using System.ComponentModel;

namespace Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;

/// <summary>Paged result for the <c>persistedOperations</c> query.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record PersistedOperationsPage(
    IReadOnlyList<PersistedOperationDto> Items,
    int TotalCount
);
