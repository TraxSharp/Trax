using Microsoft.Extensions.Logging;

namespace Trax.Api.DTOs;

/// <summary>What <c>config.setLogLevels</c> did. A list naming an unknown category sets nothing.</summary>
/// <param name="Success">Whether the levels were set.</param>
/// <param name="Count">How many categories were set; 0 on failure.</param>
/// <param name="NotApplied">
/// Categories set whose loggers still filter at another level, which happens when the host sets
/// the filter itself after Trax. Empty on failure.
/// </param>
/// <param name="Message">One line for an operator.</param>
public record SetLogLevelsResponse(
    bool Success,
    int Count,
    IReadOnlyList<string> NotApplied,
    string Message
);
