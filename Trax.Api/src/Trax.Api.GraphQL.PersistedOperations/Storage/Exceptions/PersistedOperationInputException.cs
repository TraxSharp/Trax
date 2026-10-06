namespace Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

/// <summary>
/// Thrown when an uploaded document parses and validates but is not something a persisted
/// operation can hold: a persisted document holds exactly one operation, so the id names one
/// thing to run.
/// </summary>
public sealed class PersistedOperationInputException : PersistedOperationException
{
    /// <summary>Stable code surfaced via <see cref="Code"/>.</summary>
    public const string CodeValue = "INVALID_INPUT";

    /// <inheritdoc />
    public override string Code => CodeValue;

    /// <summary>Build the exception with a human-readable message.</summary>
    public PersistedOperationInputException(string message)
        : base(message) { }
}
