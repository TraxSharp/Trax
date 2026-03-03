namespace Trax.Api.DTOs;

public record HealthStatus(
    string Status,
    string Description,
    int QueueDepth,
    int InProgress,
    int FailedLastHour,
    int DeadLetters
);
