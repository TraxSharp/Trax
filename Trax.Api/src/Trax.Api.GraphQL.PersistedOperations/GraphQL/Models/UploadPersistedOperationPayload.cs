namespace Trax.Api.GraphQL.PersistedOperations.GraphQL.Models;

/// <summary>
/// Result of <c>uploadPersistedOperation</c>. Exactly one of
/// <see cref="Operation"/> and <see cref="Errors"/> is populated.
/// </summary>
public sealed record UploadPersistedOperationPayload(
    PersistedOperationDto? Operation,
    IReadOnlyList<PersistedOperationError> Errors
)
{
    /// <summary>True when the upload succeeded.</summary>
    public bool Success => Operation is not null && Errors.Count == 0;

    internal static UploadPersistedOperationPayload Ok(PersistedOperationDto op) =>
        new(op, Array.Empty<PersistedOperationError>());

    internal static UploadPersistedOperationPayload Fail(params PersistedOperationError[] errors) =>
        new(null, errors);

    internal static UploadPersistedOperationPayload Fail(
        IEnumerable<PersistedOperationError> errors
    ) => new(null, errors.ToArray());
}
