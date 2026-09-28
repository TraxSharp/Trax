namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// The ranges a scheduler setting must stay in for the scheduler to run with it. Shared by
/// <see cref="OperationsService.UpdateSchedulerConfigAsync"/>, which refuses a patch outside them,
/// and <see cref="SchedulerConfigBootstrapHostedService"/>, which skips a persisted value outside
/// them, so a bad value can neither be stored nor stop a host that finds one already stored.
/// </summary>
/// <remarks>
/// The bounds come from what consumes each value. A polling or cleanup interval becomes a
/// <see cref="PeriodicTimer"/>, which throws below 1 ms and above about 49.7 days; the floor is
/// one second because polling the database faster than that is load, not responsiveness. A
/// timeout or retention is subtracted from the current time, and the job timeout is read as whole
/// seconds in an <see cref="int"/>, so ten years is the ceiling. Each local worker polls on its own
/// connection, so more workers than a connection pool holds only queue for connections.
/// </remarks>
internal static class SchedulerConfigLimits
{
    /// <summary>The shortest polling or cleanup interval accepted.</summary>
    public static readonly TimeSpan MinTimerInterval = TimeSpan.FromSeconds(1);

    /// <summary>The longest polling or cleanup interval accepted, inside the timer's own limit.</summary>
    public static readonly TimeSpan MaxTimerInterval = TimeSpan.FromDays(30);

    /// <summary>The shortest job timeout, stale-pending timeout or metadata retention accepted.</summary>
    public static readonly TimeSpan MinPositiveDuration = TimeSpan.FromSeconds(1);

    /// <summary>The longest timeout, delay or retention accepted.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(3650);

    /// <summary>The most local workers one host may run.</summary>
    public const int MaxLocalWorkerCount = 256;

    internal static string? TimerInterval(TimeSpan? value, string name) =>
        value is { } v && (v < MinTimerInterval || v > MaxTimerInterval)
            ? $"{name} must be between {MinTimerInterval} and {MaxTimerInterval}."
            : null;

    internal static string? PositiveDuration(TimeSpan? value, string name) =>
        value is { } v && (v < MinPositiveDuration || v > MaxDuration)
            ? $"{name} must be between {MinPositiveDuration} and {MaxDuration}."
            : null;

    internal static string? NonNegativeDuration(TimeSpan? value, string name) =>
        value is { } v && (v < TimeSpan.Zero || v > MaxDuration)
            ? $"{name} must be between {TimeSpan.Zero} and {MaxDuration}."
            : null;

    internal static string? AtLeastOne(int? value, string name) =>
        value is < 1 ? $"{name} must be at least 1." : null;

    internal static string? NotNegative(int? value, string name) =>
        value is < 0 ? $"{name} must not be negative." : null;

    internal static string? WorkerCount(int? value, string name) =>
        value is { } v && (v < 1 || v > MaxLocalWorkerCount)
            ? $"{name} must be between 1 and {MaxLocalWorkerCount}."
            : null;

    internal static string? BackoffMultiplier(double? value, string name) =>
        value is { } v && !(v >= 1.0 && double.IsFinite(v))
            ? $"{name} must be a finite number of at least 1."
            : null;
}
