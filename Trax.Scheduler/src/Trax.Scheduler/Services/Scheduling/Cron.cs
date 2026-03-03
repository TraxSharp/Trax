namespace Trax.Scheduler.Services.Scheduling;

/// <summary>
/// Provides fluent factory methods for creating cron-based schedules.
/// </summary>
/// <remarks>
/// The Cron class provides a readable, Hangfire-inspired API for defining
/// schedules based on cron expressions. Supports both standard 5-field
/// (minute granularity) and 6-field (second granularity) cron formats.
/// For complex schedules, use <see cref="Expression"/> with a raw cron string.
/// </remarks>
/// <example>
/// <code>
/// // Schedule a job to run daily at 3am
/// await scheduler.ScheduleAsync&lt;IMyTrain, MyInput&gt;(
///     "my-job",
///     new MyInput(),
///     Cron.Daily(hour: 3));
///
/// // Schedule a job with a custom 6-field cron expression (every 15 seconds)
/// await scheduler.ScheduleAsync&lt;IMyTrain, MyInput&gt;(
///     "my-job",
///     new MyInput(),
///     Cron.Expression("*/15 * * * * *"));
/// </code>
/// </example>
public static class Cron
{
    /// <summary>
    /// Creates a schedule that runs every second.
    /// </summary>
    /// <remarks>
    /// Uses 6-field cron format with seconds. The ManifestManagerPollingInterval
    /// (default: 5 seconds) limits the effective resolution of cron evaluation.
    /// </remarks>
    /// <returns>A Schedule configured to run every second</returns>
    public static Schedule Secondly() => Schedule.FromCron("* * * * * *");

    /// <summary>
    /// Creates a schedule that runs every minute.
    /// </summary>
    /// <returns>A Schedule configured to run every minute</returns>
    public static Schedule Minutely() => Schedule.FromCron("* * * * *");

    /// <summary>
    /// Creates a schedule that runs every minute at the specified second.
    /// </summary>
    /// <param name="second">The second of the minute to run (0-59)</param>
    /// <returns>A Schedule configured to run every minute at the specified second</returns>
    public static Schedule Minutely(int second) => Schedule.FromCron($"{second} * * * * *");

    /// <summary>
    /// Creates a schedule that runs hourly at the specified minute and second.
    /// </summary>
    /// <param name="minute">The minute of the hour to run (0-59). Defaults to 0.</param>
    /// <param name="second">The second of the minute to run (0-59). Defaults to 0.
    /// When non-zero, produces a 6-field cron expression with seconds.</param>
    /// <returns>A Schedule configured to run hourly</returns>
    public static Schedule Hourly(int minute = 0, int second = 0) =>
        second == 0
            ? Schedule.FromCron($"{minute} * * * *")
            : Schedule.FromCron($"{second} {minute} * * * *");

    /// <summary>
    /// Creates a schedule that runs daily at the specified time.
    /// </summary>
    /// <param name="hour">The hour of the day to run (0-23). Defaults to 0.</param>
    /// <param name="minute">The minute of the hour to run (0-59). Defaults to 0.</param>
    /// <param name="second">The second of the minute to run (0-59). Defaults to 0.
    /// When non-zero, produces a 6-field cron expression with seconds.</param>
    /// <returns>A Schedule configured to run daily</returns>
    public static Schedule Daily(int hour = 0, int minute = 0, int second = 0) =>
        second == 0
            ? Schedule.FromCron($"{minute} {hour} * * *")
            : Schedule.FromCron($"{second} {minute} {hour} * * *");

    /// <summary>
    /// Creates a schedule that runs weekly on the specified day and time.
    /// </summary>
    /// <param name="day">The day of the week to run</param>
    /// <param name="hour">The hour of the day to run (0-23). Defaults to 0.</param>
    /// <param name="minute">The minute of the hour to run (0-59). Defaults to 0.</param>
    /// <param name="second">The second of the minute to run (0-59). Defaults to 0.
    /// When non-zero, produces a 6-field cron expression with seconds.</param>
    /// <returns>A Schedule configured to run weekly</returns>
    public static Schedule Weekly(DayOfWeek day, int hour = 0, int minute = 0, int second = 0) =>
        second == 0
            ? Schedule.FromCron($"{minute} {hour} * * {(int)day}")
            : Schedule.FromCron($"{second} {minute} {hour} * * {(int)day}");

    /// <summary>
    /// Creates a schedule that runs monthly on the specified day and time.
    /// </summary>
    /// <param name="day">The day of the month to run (1-31). Defaults to 1.</param>
    /// <param name="hour">The hour of the day to run (0-23). Defaults to 0.</param>
    /// <param name="minute">The minute of the hour to run (0-59). Defaults to 0.</param>
    /// <param name="second">The second of the minute to run (0-59). Defaults to 0.
    /// When non-zero, produces a 6-field cron expression with seconds.</param>
    /// <returns>A Schedule configured to run monthly</returns>
    public static Schedule Monthly(int day = 1, int hour = 0, int minute = 0, int second = 0) =>
        second == 0
            ? Schedule.FromCron($"{minute} {hour} {day} * *")
            : Schedule.FromCron($"{second} {minute} {hour} {day} * *");

    /// <summary>
    /// Creates a schedule from a custom cron expression.
    /// </summary>
    /// <param name="cronExpression">A 5-field or 6-field cron expression</param>
    /// <returns>A Schedule configured with the specified cron expression</returns>
    /// <remarks>
    /// Supports both formats:
    /// - 5-field: minute hour day-of-month month day-of-week
    /// - 6-field: second minute hour day-of-month month day-of-week
    ///
    /// The format is auto-detected by counting fields.
    ///
    /// Examples:
    /// - "0 3 * * *" - Daily at 3am (5-field)
    /// - "0 */6 * * *" - Every 6 hours (5-field)
    /// - "*/15 * * * * *" - Every 15 seconds (6-field)
    /// - "30 0 3 * * *" - Daily at 3:00:30am (6-field)
    /// </remarks>
    public static Schedule Expression(string cronExpression) => Schedule.FromCron(cronExpression);
}
