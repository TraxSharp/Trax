using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Scheduling;

/// <summary>
/// Represents a schedule definition for recurring job execution.
/// </summary>
/// <remarks>
/// Schedule is an immutable record that encapsulates scheduling configuration.
/// Use the static factory methods <see cref="FromInterval"/> and <see cref="FromCron"/>
/// to create instances, or use the <see cref="Every"/> and <see cref="Cron"/> helper classes
/// for more readable schedule definitions.
/// </remarks>
public record Schedule
{
    /// <summary>
    /// Gets the type of schedule (Cron or Interval).
    /// </summary>
    public ScheduleType Type { get; init; }

    /// <summary>
    /// Gets the interval for Interval-type schedules.
    /// </summary>
    /// <remarks>
    /// Only used when <see cref="Type"/> is <see cref="ScheduleType.Interval"/>.
    /// </remarks>
    public TimeSpan? Interval { get; init; }

    /// <summary>
    /// Gets the cron expression for Cron-type schedules.
    /// </summary>
    /// <remarks>
    /// Only used when <see cref="Type"/> is <see cref="ScheduleType.Cron"/>.
    /// Supports both 5-field (minute granularity) and 6-field (second granularity) cron formats.
    /// </remarks>
    public string? CronExpression { get; init; }

    /// <summary>
    /// Creates a schedule from a time interval.
    /// </summary>
    /// <param name="interval">The interval between job executions</param>
    /// <returns>A new Schedule configured for interval-based execution</returns>
    /// <example>
    /// <code>
    /// var schedule = Schedule.FromInterval(TimeSpan.FromMinutes(5));
    /// </code>
    /// </example>
    public static Schedule FromInterval(TimeSpan interval) =>
        new() { Type = ScheduleType.Interval, Interval = interval };

    /// <summary>
    /// Creates a schedule from a cron expression.
    /// </summary>
    /// <param name="expression">A 5-field or 6-field cron expression</param>
    /// <returns>A new Schedule configured for cron-based execution</returns>
    /// <example>
    /// <code>
    /// var schedule = Schedule.FromCron("0 3 * * *");       // Daily at 3am (5-field)
    /// var schedule = Schedule.FromCron("*/15 * * * * *");  // Every 15 seconds (6-field)
    /// </code>
    /// </example>
    public static Schedule FromCron(string expression) =>
        new() { Type = ScheduleType.Cron, CronExpression = expression };

    /// <summary>
    /// Converts the schedule to a cron expression.
    /// </summary>
    /// <returns>A cron expression (5-field or 6-field) representing this schedule</returns>
    /// <remarks>
    /// For cron-type schedules, returns the existing expression as-is.
    /// For interval-type schedules, converts to the closest valid cron expression.
    /// Sub-minute intervals produce 6-field (seconds) cron; minute-or-above intervals
    /// produce 5-field cron.
    /// Note that cron has limited expressiveness—intervals that don't divide evenly
    /// into a minute or hour will be approximated to the nearest valid interval.
    /// </remarks>
    public string ToCronExpression()
    {
        if (Type == ScheduleType.Cron && CronExpression is not null)
            return CronExpression;

        if (Interval is null)
            return "* * * * *"; // Default to every minute

        var totalSeconds = (int)Math.Max(1, Math.Round(Interval.Value.TotalSeconds));

        // Sub-minute intervals: produce 6-field cron with seconds
        if (totalSeconds < 60)
        {
            return totalSeconds switch
            {
                1 => "* * * * * *",
                <= 30 when 60 % totalSeconds == 0 => $"*/{totalSeconds} * * * * *",
                _ => $"*/{ClosestDivisorOf60(totalSeconds)} * * * * *",
            };
        }

        var totalMinutes = (int)Math.Max(1, Math.Round(Interval.Value.TotalMinutes));

        return totalMinutes switch
        {
            1 => "* * * * *",
            <= 30 when 60 % totalMinutes == 0 => $"*/{totalMinutes} * * * *",
            60 => "0 * * * *",
            > 60 when totalMinutes % 60 == 0 && 24 % (totalMinutes / 60) == 0 =>
                $"0 */{totalMinutes / 60} * * *",
            // For intervals that don't map cleanly to cron, use the closest divisor of 60
            _ => $"*/{ClosestDivisorOf60(totalMinutes)} * * * *",
        };
    }

    /// <summary>
    /// Finds the closest divisor of 60 to the given value.
    /// Valid cron minute/second intervals must divide evenly into 60.
    /// </summary>
    private static int ClosestDivisorOf60(int value)
    {
        int[] divisors = [1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30];
        return divisors.MinBy(d => Math.Abs(d - value));
    }
}
