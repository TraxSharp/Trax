using System.Text.Json;
using Microsoft.Extensions.Logging;
using Radzen;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;

namespace Trax.Dashboard.Utilities;

public static class DashboardFormatters
{
    public static string ShortName(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
    }

    public static string FormatDuration(Metadata metadata)
    {
        if (metadata.EndTime is null)
            return "—";

        return FormatDuration((metadata.EndTime.Value - metadata.StartTime).TotalMilliseconds);
    }

    public static string FormatDuration(double ms)
    {
        if (ms < 1000)
            return $"{ms:F0}ms";
        if (ms < 60_000)
            return $"{ms / 1000:F1}s";
        return $"{ms / 60_000:F1}m";
    }

    public static string FormatSchedule(Manifest manifest) =>
        manifest.ScheduleType switch
        {
            ScheduleType.Cron => manifest.CronExpression ?? "—",
            ScheduleType.Interval => manifest.IntervalSeconds switch
            {
                null => "—",
                < 60 => $"Every {manifest.IntervalSeconds}s",
                < 3600 => $"Every {manifest.IntervalSeconds / 60}m",
                _ => $"Every {manifest.IntervalSeconds / 3600}h",
            },
            ScheduleType.Once => manifest.ScheduledAt switch
            {
                null => "Once (no time set)",
                var t when t <= DateTime.UtcNow => "Once (fired)",
                var t => $"Once at {t:g}",
            },
            _ => manifest.ScheduleType.ToString(),
        };

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

    public static BadgeStyle GetDeadLetterStatusBadgeStyle(DeadLetterStatus status) =>
        status switch
        {
            DeadLetterStatus.AwaitingIntervention => BadgeStyle.Warning,
            DeadLetterStatus.Retried => BadgeStyle.Info,
            DeadLetterStatus.Acknowledged => BadgeStyle.Success,
            _ => BadgeStyle.Light,
        };

    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
    }

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
