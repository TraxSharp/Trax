namespace Trax.Scheduler.Services.Scheduling;

/// <summary>
/// Provides fluent factory methods for creating cron-based schedules.
/// </summary>
/// <remarks>
/// The Cron class provides a readable, Hangfire-inspired API for defining
/// schedules based on cron expressions. For complex schedules, use
/// <see cref="Expression"/> with a standard 5-field cron expression.
/// </remarks>
/// <example>
/// <code>
/// // Schedule a job to run daily at 3am
/// await scheduler.ScheduleAsync&lt;IMyTrain, MyInput&gt;(
///     "my-job",
///     new MyInput(),
///     Cron.Daily(hour: 3));
///
/// // Schedule a job with a custom cron expression
/// await scheduler.ScheduleAsync&lt;IMyTrain, MyInput&gt;(
///     "my-job",
///     new MyInput(),
///     Cron.Expression("0 */6 * * *")); // Every 6 hours
/// </code>
/// </example>
public static class Cron
{
    /// <summary>
    /// Creates a schedule that runs every minute.
    /// </summary>
    /// <returns>A Schedule configured to run every minute</returns>
    public static Schedule Minutely() => Schedule.FromCron("* * * * *");

    /// <summary>
    /// Creates a schedule that runs hourly at the specified minute.
    /// </summary>
    /// <param name="minute">The minute of the hour to run (0-59). Defaults to 0.</param>
    /// <returns>A Schedule configured to run hourly</returns>
    public static Schedule Hourly(int minute = 0) => Schedule.FromCron($"{minute} * * * *");

    /// <summary>
    /// Creates a schedule that runs daily at the specified time.
    /// </summary>
    /// <param name="hour">The hour of the day to run (0-23). Defaults to 0.</param>
    /// <param name="minute">The minute of the hour to run (0-59). Defaults to 0.</param>
    /// <returns>A Schedule configured to run daily</returns>
    public static Schedule Daily(int hour = 0, int minute = 0) =>
        Schedule.FromCron($"{minute} {hour} * * *");

    /// <summary>
    /// Creates a schedule that runs weekly on the specified day and time.
    /// </summary>
    /// <param name="day">The day of the week to run</param>
    /// <param name="hour">The hour of the day to run (0-23). Defaults to 0.</param>
    /// <param name="minute">The minute of the hour to run (0-59). Defaults to 0.</param>
    /// <returns>A Schedule configured to run weekly</returns>
    public static Schedule Weekly(DayOfWeek day, int hour = 0, int minute = 0) =>
        Schedule.FromCron($"{minute} {hour} * * {(int)day}");

    /// <summary>
    /// Creates a schedule that runs monthly on the specified day and time.
    /// </summary>
    /// <param name="day">The day of the month to run (1-31). Defaults to 1.</param>
    /// <param name="hour">The hour of the day to run (0-23). Defaults to 0.</param>
    /// <param name="minute">The minute of the hour to run (0-59). Defaults to 0.</param>
    /// <returns>A Schedule configured to run monthly</returns>
    public static Schedule Monthly(int day = 1, int hour = 0, int minute = 0) =>
        Schedule.FromCron($"{minute} {hour} {day} * *");

    /// <summary>
    /// Creates a schedule from a custom cron expression.
    /// </summary>
    /// <param name="cronExpression">A standard 5-field cron expression</param>
    /// <returns>A Schedule configured with the specified cron expression</returns>
    /// <remarks>
    /// The cron expression should use the standard 5-field format:
    /// minute hour day-of-month month day-of-week
    ///
    /// Examples:
    /// - "0 3 * * *" - Daily at 3am
    /// - "0 */6 * * *" - Every 6 hours
    /// - "0 0 * * 0" - Weekly on Sunday at midnight
    /// - "0 0 1 * *" - Monthly on the 1st at midnight
    /// </remarks>
    public static Schedule Expression(string cronExpression) => Schedule.FromCron(cronExpression);
}
