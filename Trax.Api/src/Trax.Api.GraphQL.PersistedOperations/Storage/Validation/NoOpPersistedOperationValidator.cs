namespace Trax.Api.GraphQL.PersistedOperations.Storage.Validation;

/// <summary>
/// Default validator for hosts that do not have a HotChocolate schema in
/// process (e.g. <c>AddPersistedOperationStore</c> in a console uploader).
/// Performs no checks; the document is taken at face value. The shape-diff
/// guardrail still runs at the storage layer.
/// </summary>
public sealed class NoOpPersistedOperationValidator : IPersistedOperationValidator
{
    /// <inheritdoc />
    public Task ValidateAsync(string document, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled(ct);
        return Task.CompletedTask;
    }
}
