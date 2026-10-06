namespace Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

/// <summary>
/// Thrown when an uploaded document parses but fails to validate against the
/// server schema (unknown field, wrong variable type, missing required
/// variable, etc.). Carries the full set of failures so the dashboard can
/// surface all errors at once instead of one-at-a-time.
/// </summary>
public sealed class PersistedOperationValidationException : PersistedOperationException
{
    /// <summary>Stable code surfaced via <see cref="Code"/>.</summary>
    public const string CodeValue = "SCHEMA_VALIDATION_FAILED";

    /// <inheritdoc />
    public override string Code => CodeValue;

    /// <summary>The full set of failures detected in the document.</summary>
    public IReadOnlyList<ValidationFailure> Failures { get; }

    /// <summary>Build the exception with at least one failure.</summary>
    public PersistedOperationValidationException(IReadOnlyList<ValidationFailure> failures)
        : base(BuildMessage(failures))
    {
        Failures = failures;
    }

    private static string BuildMessage(IReadOnlyList<ValidationFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        if (failures.Count == 0)
            throw new ArgumentException(
                "PersistedOperationValidationException requires at least one failure.",
                nameof(failures)
            );

        if (failures.Count == 1)
            return $"Persisted operation document failed schema validation: {failures[0].Message}";
        return $"Persisted operation document failed schema validation with {failures.Count} errors. First: {failures[0].Message}";
    }
}
