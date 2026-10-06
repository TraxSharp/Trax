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
    // Only the factories build a schedule, so its type always matches the field that is set.
    internal Schedule() { }

    /// <summary>
    /// Gets the type of schedule (Cron or Interval).
    /// </summary>
    public ScheduleType Type { get; internal init; }

    /// <summary>
    /// Gets the interval for Interval-type schedules.
    /// </summary>
    /// <remarks>
    /// Only used when <see cref="Type"/> is <see cref="ScheduleType.Interval"/>.
    /// </remarks>
    public TimeSpan? Interval { get; internal init; }

    /// <summary>
    /// Gets the cron expression for Cron-type schedules.
    /// </summary>
    /// <remarks>
    /// Only used when <see cref="Type"/> is <see cref="ScheduleType.Cron"/>.
    /// Supports both 5-field (minute granularity) and 6-field (second granularity) cron formats.
    /// The expression is evaluated in UTC.
    /// </remarks>
    public string? CronExpression { get; internal init; }

    /// <summary>
    /// Gets the maximum random delay added to each scheduled run.
    /// </summary>
    /// <remarks>
    /// When set, after each successful execution the scheduler adds a random delay
    /// of <c>[0, Variance]</c> to the next scheduled time. This prevents thundering-herd
    /// problems and makes scheduling patterns less predictable. Only applies to
    /// <see cref="ScheduleType.Interval"/> and <see cref="ScheduleType.Cron"/> schedules.
    /// </remarks>
    public TimeSpan? Variance { get; init; }

    /// <summary>
    /// Creates a schedule from a time interval.
    /// </summary>
    /// <param name="interval">The interval between job executions, at least one second</param>
    /// <returns>A new Schedule configured for interval-based execution</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="interval"/> is shorter than one second.
    /// </exception>
    /// <example>
    /// <code>
    /// var schedule = Schedule.FromInterval(TimeSpan.FromMinutes(5));
    /// </code>
    /// </example>
    public static Schedule FromInterval(TimeSpan interval)
    {
        // The manifest stores whole seconds, so anything shorter would be stored as zero.
        if (interval < TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                interval,
                "A schedule interval must be at least one second."
            );

        return new() { Type = ScheduleType.Interval, Interval = interval };
    }

    /// <summary>
    /// Creates a schedule from a cron expression.
    /// </summary>
    /// <param name="expression">A 5-field or 6-field cron expression, evaluated in UTC</param>
    /// <returns>A new Schedule configured for cron-based execution</returns>
    /// <remarks>
    /// The expression is parsed here, so one that cannot fire (an hour of 25, a sixth field of
    /// garbage) fails at the call that states it rather than being stored. So does a valid one
    /// with no occurrence in the next ten years, such as
    /// <c>0 0 30 2 *</c> (February 30th). A new cron schedule first runs at its first occurrence
    /// after it is scheduled, not on the next poll.
    /// </remarks>
    /// <exception cref="FormatException">
    /// <paramref name="expression"/> is not a valid 5-field or 6-field cron expression, or it
    /// never fires within ten years from now.
    /// </exception>
    /// <example>
    /// <code>
    /// var schedule = Schedule.FromCron("0 3 * * *");       // Daily at 03:00 UTC (5-field)
    /// var schedule = Schedule.FromCron("*/15 * * * * *");  // Every 15 seconds (6-field)
    /// </code>
    /// </example>
    public static Schedule FromCron(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var parsed = CronParser.Parse(expression);

        var now = DateTime.UtcNow;
        var next = parsed.GetNextOccurrence(now, TimeZoneInfo.Utc);
        if (next is null || next > now.AddYears(CronSearchWindowYears))
            throw new FormatException(
                $"Cron expression '{expression}' never fires: it has no occurrence in the next "
                    + $"{CronSearchWindowYears} years. Check the day of the month against the months "
                    + "it names (February has no 30th, April no 31st)."
            );

        return new() { Type = ScheduleType.Cron, CronExpression = expression };
    }

    /// <summary>
    /// How far ahead <see cref="FromCron"/> looks for a cron expression's next occurrence before
    /// refusing it as one that never fires.
    /// </summary>
    internal const int CronSearchWindowYears = 10;

    /// <summary>
    /// Returns a copy of this schedule with the specified variance (jitter).
    /// </summary>
    /// <param name="variance">The maximum random delay added after each scheduled run</param>
    /// <returns>A new Schedule with the variance applied</returns>
    /// <example>
    /// <code>
    /// var schedule = Every.Minutes(5).WithVariance(TimeSpan.FromMinutes(2));
    /// // Runs every 5–7 minutes (base interval + 0–2 min random delay)
    /// </code>
    /// </example>
    public Schedule WithVariance(TimeSpan variance) => this with { Variance = variance };

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
