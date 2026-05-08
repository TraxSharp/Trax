using Microsoft.Extensions.Logging;

namespace Trax.Api.DTOs;

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
