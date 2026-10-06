namespace Trax.Api.GraphQL.PersistedOperations.Storage.Exceptions;

/// <summary>
/// Thrown when an uploaded document does not parse as valid GraphQL syntax.
/// Carries the originating line/column when the underlying parser exposed
/// them, so the dashboard can highlight the offending location.
/// </summary>
public sealed class PersistedOperationParseException : PersistedOperationException
{
    /// <summary>Stable code surfaced via <see cref="Code"/>.</summary>
    public const string CodeValue = "PARSE_FAILED";

    /// <inheritdoc />
    public override string Code => CodeValue;

    /// <summary>1-based line number of the syntax error, when known.</summary>
    public int? Line { get; }

    /// <summary>1-based column number of the syntax error, when known.</summary>
    public int? Column { get; }

    /// <summary>The parser's original message, preserved without prefixing.</summary>
    public string OriginalMessage { get; }

    /// <summary>
    /// Build the exception from a parser failure.
    /// </summary>
    public PersistedOperationParseException(
        string originalMessage,
        int? line,
        int? column,
        Exception? inner = null
    )
        : base(
            BuildMessage(originalMessage, line, column),
            inner ?? new InvalidOperationException()
        )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalMessage);
        OriginalMessage = originalMessage;
        Line = line;
        Column = column;
    }

    private static string BuildMessage(string originalMessage, int? line, int? column)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalMessage);
        if (line.HasValue && column.HasValue)
            return $"Persisted operation document failed to parse at line {line}, column {column}: {originalMessage}";
        return $"Persisted operation document failed to parse: {originalMessage}";
    }
}
