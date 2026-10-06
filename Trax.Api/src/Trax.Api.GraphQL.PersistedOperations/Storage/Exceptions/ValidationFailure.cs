namespace Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

/// <summary>
/// A single schema-validation failure reported by the validator. Multiple
/// failures can be returned from one document (e.g. two unknown fields).
/// </summary>
/// <param name="Message">Human-readable message from the validator.</param>
/// <param name="Locations">1-based (line, column) pairs the failure points at; empty when not available.</param>
/// <param name="Path">Response path components leading to the failure; empty when not applicable.</param>
public sealed record ValidationFailure(
    string Message,
    IReadOnlyList<ValidationFailureLocation> Locations,
    IReadOnlyList<object> Path
)
{
    /// <summary>Build a failure with no location or path data.</summary>
    public static ValidationFailure FromMessage(string message) =>
        new(message, Array.Empty<ValidationFailureLocation>(), Array.Empty<object>());
}

/// <summary>1-based location in the source document.</summary>
public readonly record struct ValidationFailureLocation(int Line, int Column);
