using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Trax.StateMachine;

/// <summary>
/// ISO-8601 durations as a timer reads and writes them: days, hours, minutes and seconds (<c>P1DT2H30M</c>,
/// <c>PT0.5S</c>), never negative, and with no years, months or weeks, whose length is not fixed. The TypeScript twin
/// parses the same subset, so both engines accept and refuse the same text. The digits are ASCII only: .NET's
/// <c>\d</c> matches every Unicode decimal digit, which neither the twin nor <see cref="decimal"/> reads.
/// </summary>
internal static partial class IsoDuration
{
    [GeneratedRegex(
        @"^P(?!$)(?:(?<days>[0-9]{1,9})D)?(?:T(?=[0-9])(?:(?<hours>[0-9]{1,9})H)?(?:(?<minutes>[0-9]{1,9})M)?(?:(?<seconds>[0-9]{1,9})(?:\.(?<fraction>[0-9]{1,7}))?S)?)?\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex Pattern();

    /// <summary>Parses <paramref name="text"/>; false for anything outside the subset, never a throw.</summary>
    public static bool TryParse(string? text, out TimeSpan duration)
    {
        duration = default;
        if (text is null)
            return false;
        var match = Pattern().Match(text);
        if (!match.Success)
            return false;

        // Every component is at most nine digits, so the total in ticks fits a decimal, and anything past what a
        // TimeSpan holds is refused rather than wrapped.
        // A component that does not read as a number is refused, never thrown, whatever the pattern let through.
        // Every group is named, so the five are numbered 1 to 5 in the order they open: days to the fraction.
        var parts = new decimal[6];
        for (var group = 1; group <= 5; group++)
        {
            if (!match.Groups[group].Success)
                continue;
            var digits = group == 5 ? "0." + match.Groups[group].Value : match.Groups[group].Value;
            if (
                !decimal.TryParse(
                    digits,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out parts[group]
                )
            )
                return false;
        }
        var seconds = parts[1] * 86_400m + parts[2] * 3_600m + parts[3] * 60m + parts[4] + parts[5];
        var ticks = seconds * TimeSpan.TicksPerSecond;
        if (ticks > TimeSpan.MaxValue.Ticks)
            return false;
        duration = TimeSpan.FromTicks((long)ticks);
        return true;
    }

    /// <summary>Writes <paramref name="duration"/> as the IR and the context carry it, e.g. <c>PT10M</c>.</summary>
    public static string Format(TimeSpan duration) => XmlConvert.ToString(duration);
}
