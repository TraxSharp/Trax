using Microsoft.Extensions.Logging;

namespace Trax.Api.DTOs;

/// <summary>
/// One log record written by a train while it ran, as the paged log read returns it.
/// </summary>
/// <param name="Id">The log record's id; pages are keyed on it.</param>
/// <param name="MetadataId">The id of the execution that wrote it.</param>
/// <param name="EventId">The <see cref="Microsoft.Extensions.Logging.EventId"/> the message was logged with (0 when none was given).</param>
/// <param name="Level">The level it was logged at.</param>
/// <param name="Category">The logger category, usually the full name of the class that logged it.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The exception message, when one was logged with the record.</param>
/// <param name="StackTrace">The stack trace of that exception, when there is one.</param>
public record LogEntry(
    long Id,
    long MetadataId,
    int EventId,
    LogLevel Level,
    string Category,
    string Message,
    string? Exception,
    string? StackTrace
);
