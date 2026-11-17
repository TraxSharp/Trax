using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Services.LogLevels;

/// <summary>What <see cref="ILogLevelService.SetLogLevels"/> did.</summary>
/// <param name="Success">Whether the levels were set.</param>
/// <param name="Count">How many categories were set; 0 on failure.</param>
/// <param name="NotApplied">
/// The categories set whose loggers still filter at another level. Empty on failure.
/// </param>
/// <param name="Message">One line for an operator. Always set.</param>
public record LogLevelUpdateResult(
    bool Success,
    int Count,
    IReadOnlyList<string> NotApplied,
    string Message
);
