using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Services.Operations;

/// <summary>One log entry, as <see cref="IOperationsService.GetLogsAsync"/> returns it.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/i-operations-service#logrecord">SDK reference</seealso>
public record LogRecord(
    long Id,
    long MetadataId,
    int EventId,
    LogLevel Level,
    string Category,
    string Message,
    string? Exception,
    string? StackTrace
);
