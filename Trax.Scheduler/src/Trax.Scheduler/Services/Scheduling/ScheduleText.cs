using System.Globalization;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;

namespace Trax.Scheduler.Services.Scheduling;

/// <summary>
/// A schedule in plain words, the way an operator reads it: "Daily at 02:00 UTC", "Every 5
/// minutes". A cron expression with no plain reading is shown as itself.
/// </summary>
internal static class ScheduleText
{
    /// <summary>Describes a schedule built by <see cref="Every"/>, <see cref="Cron"/> or <see cref="Schedule"/>.</summary>
    public static string Describe(Schedule schedule) =>
        WithVariance(
            schedule.Type == ScheduleType.Cron
                ? DescribeCron(schedule.CronExpression)
                : DescribeInterval((int?)schedule.Interval?.TotalSeconds),
            (int?)schedule.Variance?.TotalSeconds
        );

    /// <summary>
    /// Describes a manifest's schedule. <paramref name="parentName"/> names the manifest a
    /// dependent runs after; when it is null the text says "its parent".
    /// </summary>
    public static string Describe(Manifest manifest, string? parentName = null)
    {
        var parent = string.IsNullOrWhiteSpace(parentName) ? "its parent" : parentName;
        return manifest.ScheduleType switch
        {
            ScheduleType.Cron => WithVariance(
                DescribeCron(manifest.CronExpression),
                manifest.VarianceSeconds
            ),
            ScheduleType.Interval => WithVariance(
                DescribeInterval(manifest.IntervalSeconds),
                manifest.VarianceSeconds
            ),
            ScheduleType.Once => manifest.ScheduledAt is { } at
                ? $"Once at {FormatInstant(at)} UTC"
                : "Once (no time set)",
            ScheduleType.Dependent => $"After {parent} succeeds",
            ScheduleType.DormantDependent => $"When {parent} activates it",
            ScheduleType.OnDemand => "On demand",
            _ => "Only when triggered",
        };
    }

    /// <summary>A cron expression in words, or <c>Cron &lt;expression&gt; (UTC)</c> when it has none.</summary>
    public static string DescribeCron(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return "Cron (no expression)";

        var fields = expression.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is not (5 or 6) || CronParser.TryParse(expression) is null)
            return $"Cron {expression.Trim()} (UTC)";

        string? second = null;
        if (fields.Length == 6)
        {
            second = fields[0];
            fields = fields[1..];
        }

        return DescribeFields(second, fields[0], fields[1], fields[2], fields[3], fields[4])
            ?? $"Cron {expression.Trim()} (UTC)";
    }

    /// <summary>An interval in whole seconds, in words.</summary>
    public static string DescribeInterval(int? seconds) =>
        seconds is > 0
            ? $"Every {Duration(seconds.Value, singularWithoutCount: true)}"
            : "Interval (not set)";

    private static string? DescribeFields(
        string? second,
        string minute,
        string hour,
        string dayOfMonth,
        string month,
        string dayOfWeek
    )
    {
        var everyDay = IsAny(dayOfMonth) && IsAny(month) && IsAny(dayOfWeek);
        var secondValue = 0;

        if (second is not null && !IsNumber(second, out secondValue))
        {
            // Sub-minute schedules: only "every second" and "every n seconds" have a plain reading.
            if (!(IsAny(minute) && IsAny(hour) && everyDay))
                return null;
            if (IsAny(second))
                return "Every second";
            return Step(second) is { } n ? $"Every {n} seconds" : null;
        }

        if (IsAny(hour) && everyDay)
        {
            if (IsAny(minute))
                return secondValue == 0 ? "Every minute" : $"Every minute at second {secondValue}";
            if (secondValue == 0 && Step(minute) is { } everyMinutes)
                return $"Every {everyMinutes} minutes";
            if (IsNumber(minute, out var atMinute))
                return $"Every hour at :{atMinute:00}{SecondSuffix(secondValue)}";
            return null;
        }

        if (!IsNumber(minute, out var m))
            return null;

        if (Step(hour) is { } everyHours && everyDay)
            return $"Every {everyHours} hours at :{m:00}{SecondSuffix(secondValue)}";

        var hours = NumberList(hour, 0, 23);
        if (hours is null)
            return null;

        var times = JoinWords(hours.Select(h => Time(h, m, secondValue)).ToList());
        var at = $"at {times} UTC";

        if (everyDay)
            return $"Daily {at}";

        if (IsAny(dayOfMonth) && IsAny(month))
        {
            if (dayOfWeek == "1-5")
                return $"Weekdays {at}";
            var days = NumberList(dayOfWeek, 0, 7);
            if (days is null)
                return null;
            var distinct = days.Select(d => d % 7).Distinct().Order().ToList();
            if (distinct.SequenceEqual([0, 6]))
                return $"Weekends {at}";
            return $"Every {JoinWords(distinct.Select(d => ((DayOfWeek)d).ToString()).ToList())} {at}";
        }

        if (IsAny(dayOfWeek) && IsNumber(dayOfMonth, out var dom) && dom is >= 1 and <= 31)
        {
            if (IsAny(month))
                return $"Monthly on the {Ordinal(dom)} {at}";
            if (IsNumber(month, out var mon) && mon is >= 1 and <= 12)
                return $"Yearly on {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(mon)} {dom} {at}";
        }

        return null;
    }

    private static string WithVariance(string text, int? varianceSeconds) =>
        varianceSeconds is > 0
            ? $"{text}, up to {Duration(varianceSeconds.Value, singularWithoutCount: false)} late"
            : text;

    /// <summary>
    /// A whole number of seconds in the largest unit that divides it: "5 minutes", "90 seconds".
    /// With <paramref name="singularWithoutCount"/>, a count of one drops the number ("minute").
    /// </summary>
    private static string Duration(int seconds, bool singularWithoutCount)
    {
        var (count, unit) = seconds switch
        {
            _ when seconds % 86_400 == 0 => (seconds / 86_400, "day"),
            _ when seconds % 3_600 == 0 => (seconds / 3_600, "hour"),
            _ when seconds % 60 == 0 => (seconds / 60, "minute"),
            _ => (seconds, "second"),
        };

        if (count == 1)
            return singularWithoutCount ? unit : $"1 {unit}";
        return $"{count} {unit}s";
    }

    private static string FormatInstant(DateTime at) =>
        at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Time(int hour, int minute, int second) =>
        second == 0 ? $"{hour:00}:{minute:00}" : $"{hour:00}:{minute:00}:{second:00}";

    private static string SecondSuffix(int second) => second == 0 ? "" : $":{second:00}";

    private static bool IsAny(string field) => field is "*" or "?";

    private static bool IsNumber(string field, out int value) =>
        int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>The n of a <c>*/n</c> field, when n is above one.</summary>
    private static int? Step(string field) =>
        field.StartsWith("*/", StringComparison.Ordinal) && IsNumber(field[2..], out var n) && n > 1
            ? n
            : null;

    /// <summary>The values of a comma-separated list of plain numbers within a range, or null.</summary>
    private static List<int>? NumberList(string field, int min, int max)
    {
        var values = new List<int>();
        foreach (var part in field.Split(','))
        {
            if (!IsNumber(part, out var value) || value < min || value > max)
                return null;
            values.Add(value);
        }
        return values;
    }

    private static string JoinWords(IReadOnlyList<string> items) =>
        items.Count switch
        {
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}",
        };

    private static string Ordinal(int n) =>
        (n % 100) is 11 or 12 or 13
            ? $"{n}th"
            : (n % 10) switch
            {
                1 => $"{n}st",
                2 => $"{n}nd",
                3 => $"{n}rd",
                _ => $"{n}th",
            };
}
