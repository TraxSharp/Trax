using Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

namespace Trax.Api.GraphQL.PersistedOperations.Storage.Validation;

/// <summary>
/// Validates a GraphQL document string before it is persisted. Runs at
/// upload time so that errors surface to the operator immediately rather
/// than at execution time when a client tries to run the operation.
/// </summary>
/// <remarks>
/// Two implementations ship in this package:
/// <list type="bullet">
///   <item><see cref="NoOpPersistedOperationValidator"/>: the default in
///     <c>AddPersistedOperationStore</c> (admin tooling without a schema).</item>
///   <item>HotChocolate-backed validator: registered automatically by
///     <c>UsePersistedOperations</c>. Validates against the live schema.</item>
/// </list>
/// Implementations throw <see cref="PersistedOperationParseException"/> for
/// syntax errors and <see cref="PersistedOperationValidationException"/> for
/// schema-validation errors. Callers are expected to let these propagate;
/// the storage layer ensures no DB write or broadcast occurs on failure.
/// </remarks>
public interface IPersistedOperationValidator
{
    /// <summary>
    /// Validate <paramref name="document"/>. Returns successfully if the
    /// document is valid; throws otherwise.
    /// </summary>
    Task ValidateAsync(string document, CancellationToken ct);
}
