using System.Text.Json;
using Microsoft.Extensions.Logging;
using Radzen;
using Trax.Dashboard.Models;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;

namespace Trax.Dashboard.Utilities;

/// <summary>
/// Display formatting shared by the dashboard's pages: short train names, durations, schedules,
/// JSON, and the Radzen badge style for each state. Infrastructure for the dashboard's own
/// markup; not intended to be called directly.
/// </summary>
internal static class DashboardFormatters
{
    /// <summary>
    /// Returns the part of a dotted name after the last dot, so a train's interface FullName
    /// becomes its bare interface name. A name without a dot is returned unchanged.
    /// </summary>
    /// <param name="fullName">A type or train name.</param>
    public static string ShortName(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
    }

    /// <summary>
    /// Formats how long a run took, from its start to its end time, using
    /// <see cref="FormatDuration(double)"/>. Returns an em dash while the run has no end time.
    /// </summary>
    /// <param name="metadata">The run.</param>
    public static string FormatDuration(Metadata metadata)
    {
        if (metadata.EndTime is null)
            return "—";

        return FormatDuration((metadata.EndTime.Value - metadata.StartTime).TotalMilliseconds);
    }

    /// <summary>
    /// <see cref="FormatDuration(Metadata)"/> for a run as a grid shows it.
    /// </summary>
    /// <param name="run">The run.</param>
    public static string FormatDuration(RunRow run) =>
        run.EndTime is null
            ? "—"
            : FormatDuration((run.EndTime.Value - run.StartTime).TotalMilliseconds);

    /// <summary>
    /// Formats a duration as whole milliseconds under a second (<c>850ms</c>), seconds with one
    /// decimal under a minute (<c>12.3s</c>), and minutes with one decimal beyond that
    /// (<c>75.0m</c>, never hours).
    /// </summary>
    /// <param name="ms">The duration in milliseconds.</param>
    public static string FormatDuration(double ms)
    {
        if (ms < 1000)
            return $"{ms:F0}ms";
        if (ms < 60_000)
            return $"{ms / 1000:F1}s";
        return $"{ms / 60_000:F1}m";
    }

    /// <summary>
    /// Describes a manifest's schedule: the cron expression; for an interval, <c>Every</c> with its
    /// hours, minutes and seconds, the zero ones left out and nothing rounded (so 90 seconds reads
    /// <c>Every 1m 30s</c>); or, for a one-off, <c>Once at {time}</c>, <c>Once (fired)</c> once the
    /// time has passed, or <c>Once (no time set)</c>. Missing values give an em dash; other
    /// schedule types give the enum name.
    /// </summary>
    /// <param name="manifest">The manifest.</param>
    public static string FormatSchedule(Manifest manifest) =>
        manifest.ScheduleType switch
        {
            ScheduleType.Cron => manifest.CronExpression ?? "—",
            ScheduleType.Interval => manifest.IntervalSeconds switch
            {
                null => "—",
                <= 0 => $"Every {manifest.IntervalSeconds}s",
                { } seconds => $"Every {FormatIntervalSeconds(seconds)}",
            },
            ScheduleType.Once => manifest.ScheduledAt switch
            {
                null => "Once (no time set)",
                var t when t <= DateTime.UtcNow => "Once (fired)",
                var t => $"Once at {t:g}",
            },
            _ => manifest.ScheduleType.ToString(),
        };

    /// <summary>
    /// A whole number of seconds as hours, minutes and seconds, leaving out the zero parts:
    /// 90 is "1m 30s", 3600 is "1h", 3661 is "1h 1m 1s". Nothing is rounded away.
    /// </summary>
    private static string FormatIntervalSeconds(int seconds)
    {
        var parts = new List<string>(3);
        if (seconds / 3600 is var hours and > 0)
            parts.Add($"{hours}h");
        if (seconds % 3600 / 60 is var minutes and > 0)
            parts.Add($"{minutes}m");
        if (seconds % 60 is var rest and > 0)
            parts.Add($"{rest}s");
        return string.Join(" ", parts);
    }

    /// <summary>
    /// Re-indents <paramref name="json"/> for display. Returns the input unchanged when it is not
    /// valid JSON, so it is safe on arbitrary stored text.
    /// </summary>
    /// <param name="json">The text to format.</param>
    public static string FormatJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(
                doc,
                new JsonSerializerOptions { WriteIndented = true }
            );
        }
        catch
        {
            return json;
        }
    }

    /// <summary>
    /// The badge colour for a run's state: success for completed, danger for failed, info for in
    /// progress, warning for pending and cancelled, light for anything else.
    /// </summary>
    /// <param name="state">The run's state.</param>
    public static BadgeStyle GetStateBadgeStyle(TrainState state) =>
        state switch
        {
            TrainState.Completed => BadgeStyle.Success,
            TrainState.Failed => BadgeStyle.Danger,
            TrainState.InProgress => BadgeStyle.Info,
            TrainState.Pending => BadgeStyle.Warning,
            TrainState.Cancelled => BadgeStyle.Warning,
            _ => BadgeStyle.Light,
        };

    /// <summary>
    /// The badge colour for a dead letter's status: warning while awaiting intervention, info once
    /// retried, success once acknowledged, light for anything else.
    /// </summary>
    /// <param name="status">The dead letter's status.</param>
    public static BadgeStyle GetDeadLetterStatusBadgeStyle(DeadLetterStatus status) =>
        status switch
        {
            DeadLetterStatus.AwaitingIntervention => BadgeStyle.Warning,
            DeadLetterStatus.Retried => BadgeStyle.Info,
            DeadLetterStatus.Acknowledged => BadgeStyle.Success,
            _ => BadgeStyle.Light,
        };

    /// <summary>
    /// Formats an uptime with its two largest units: <c>3d 4h</c>, <c>5h 12m</c> or <c>7m 30s</c>.
    /// </summary>
    /// <param name="uptime">The elapsed time.</param>
    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
    }

    /// <summary>
    /// The badge colour for a work queue entry's status: info while queued, success once
    /// dispatched, warning when cancelled, light for anything else.
    /// </summary>
    /// <param name="status">The work queue entry's status.</param>
    public static BadgeStyle GetWorkQueueStatusBadgeStyle(WorkQueueStatus status) =>
        status switch
        {
            WorkQueueStatus.Queued => BadgeStyle.Info,
            WorkQueueStatus.Dispatched => BadgeStyle.Success,
            WorkQueueStatus.Cancelled => BadgeStyle.Warning,
            _ => BadgeStyle.Light,
        };

    /// <summary>
    /// The badge colour for a log entry's level: danger for critical and error, warning for
    /// warning, info for information, light for debug, trace and anything else.
    /// </summary>
    /// <param name="level">The log level.</param>
    public static BadgeStyle GetLogLevelBadgeStyle(LogLevel level) =>
        level switch
        {
            LogLevel.Critical => BadgeStyle.Danger,
            LogLevel.Error => BadgeStyle.Danger,
            LogLevel.Warning => BadgeStyle.Warning,
            LogLevel.Information => BadgeStyle.Info,
            LogLevel.Debug => BadgeStyle.Light,
            LogLevel.Trace => BadgeStyle.Light,
            _ => BadgeStyle.Light,
        };
}
