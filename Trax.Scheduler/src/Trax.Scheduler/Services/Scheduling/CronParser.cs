using Cronos;

namespace Trax.Scheduler.Services.Scheduling;

/// <summary>
/// Internal helper for parsing cron expressions with auto-detection of 5-field vs 6-field format.
/// </summary>
internal static class CronParser
{
    /// <summary>
    /// Parses a cron expression string, auto-detecting whether it is 5-field (standard)
    /// or 6-field (with seconds).
    /// </summary>
    /// <param name="expression">The cron expression to parse</param>
    /// <returns>The parsed CronExpression, or null if parsing fails</returns>
    public static CronExpression? TryParse(string expression)
    {
        var format = DetectFormat(expression);
        if (format is null)
            return null;

        try
        {
            return CronExpression.Parse(expression, format.Value);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a cron expression string, throwing on failure.
    /// </summary>
    /// <param name="expression">The cron expression to parse</param>
    /// <returns>The parsed CronExpression</returns>
    /// <exception cref="FormatException">Thrown when the expression is not a valid 5 or 6 field cron expression</exception>
    public static CronExpression Parse(string expression)
    {
        var format =
            DetectFormat(expression)
            ?? throw new FormatException(
                $"Invalid cron expression: '{expression}'. Expected 5 or 6 space-separated fields."
            );

        return CronExpression.Parse(expression, format);
    }

    /// <summary>
    /// Gets the next occurrence after the given time.
    /// </summary>
    /// <param name="expression">The cron expression</param>
    /// <param name="after">The time after which to find the next occurrence</param>
    /// <returns>The next occurrence, or null if the expression is invalid or has no future occurrences</returns>
    public static DateTime? GetNextOccurrence(string expression, DateTime after)
    {
        var parsed = TryParse(expression);
        return parsed?.GetNextOccurrence(after, TimeZoneInfo.Utc);
    }

    /// <summary>
    /// Detects whether the expression is 5-field or 6-field based on the number of fields.
    /// </summary>
    /// <param name="expression">The cron expression</param>
    /// <returns>The detected format, or null if the field count is not 5 or 6</returns>
    public static CronFormat? DetectFormat(string expression)
    {
        var parts = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            5 => CronFormat.Standard,
            6 => CronFormat.IncludeSeconds,
            _ => null,
        };
    }

    /// <summary>
    /// Returns true if the expression uses 6-field format (includes seconds).
    /// </summary>
    /// <param name="expression">The cron expression</param>
    /// <returns>True if the expression has 6 fields</returns>
    public static bool IncludesSeconds(string expression) =>
        DetectFormat(expression) == CronFormat.IncludeSeconds;
}
