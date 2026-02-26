namespace Trax.Scheduler.Services.Scheduling;

/// <summary>
/// Provides fluent factory methods for creating interval-based schedules.
/// </summary>
/// <remarks>
/// The Every class provides a readable, Hangfire-inspired API for defining
/// recurring schedules based on time intervals.
/// </remarks>
/// <example>
/// <code>
/// // Schedule a job to run every 30 seconds
/// await scheduler.ScheduleAsync&lt;IMyWorkflow, MyInput&gt;(
///     "my-job",
///     new MyInput(),
///     Every.Seconds(30));
///
/// // Schedule a job to run every 5 minutes
/// await scheduler.ScheduleAsync&lt;IMyWorkflow, MyInput&gt;(
///     "my-job",
///     new MyInput(),
///     Every.Minutes(5));
/// </code>
/// </example>
public static class Every
{
    /// <summary>
    /// Creates a schedule that runs at the specified interval in seconds.
    /// </summary>
    /// <param name="seconds">The number of seconds between executions</param>
    /// <returns>A Schedule configured to run at the specified interval</returns>
    public static Schedule Seconds(int seconds) =>
        Schedule.FromInterval(TimeSpan.FromSeconds(seconds));

    /// <summary>
    /// Creates a schedule that runs at the specified interval in minutes.
    /// </summary>
    /// <param name="minutes">The number of minutes between executions</param>
    /// <returns>A Schedule configured to run at the specified interval</returns>
    public static Schedule Minutes(int minutes) =>
        Schedule.FromInterval(TimeSpan.FromMinutes(minutes));

    /// <summary>
    /// Creates a schedule that runs at the specified interval in hours.
    /// </summary>
    /// <param name="hours">The number of hours between executions</param>
    /// <returns>A Schedule configured to run at the specified interval</returns>
    public static Schedule Hours(int hours) => Schedule.FromInterval(TimeSpan.FromHours(hours));

    /// <summary>
    /// Creates a schedule that runs at the specified interval in days.
    /// </summary>
    /// <param name="days">The number of days between executions</param>
    /// <returns>A Schedule configured to run at the specified interval</returns>
    public static Schedule Days(int days) => Schedule.FromInterval(TimeSpan.FromDays(days));
}
