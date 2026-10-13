using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Trax.Effect.StateMachine;

/// <summary>The limits every state's timer is held to.</summary>
public static class MachineTimers
{
    /// <summary>
    /// The longest a timer may wait: 366 days. A fixed duration above it, or a context duration whose declared maximum
    /// is above it, refuses to build. A timer that waits longer than this is a schedule, not a step of a machine.
    /// </summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(366);
}

/// <summary>
/// A state's timer: from <see cref="State"/>, the server moves the instance to <see cref="Target"/> once the state has
/// been held for its duration. The duration is <see cref="Fixed"/>, or read when the state is entered from the
/// context field <see cref="Field"/> (or by <see cref="Computed"/>, which the IR cannot carry), bounded by
/// <see cref="Min"/> and <see cref="Max"/>.
/// </summary>
internal sealed record TimerDefinition<TState>(
    TState State,
    TState Target,
    TimeSpan? Fixed,
    string? Field,
    TimeSpan Min,
    TimeSpan Max,
    Func<JsonObject, TimeSpan?>? Computed
)
    where TState : struct, Enum
{
    /// <summary>The timer trigger, <c>&lt;State&gt;.after</c>.</summary>
    public string TriggerName => TimerTriggers.Name(State);

    /// <summary>
    /// The duration the context <paramref name="context"/> entering <see cref="State"/> arms the timer with, or why it
    /// cannot arm it (the field is missing, not an ISO-8601 duration, or outside its declared bounds). Never throws.
    /// </summary>
    public (TimeSpan? Duration, string? Problem) DurationFor(JsonObject context)
    {
        if (Fixed is { } fixedDuration)
            return (fixedDuration, null);

        TimeSpan? read;
        if (Field is { } field)
        {
            if (
                context[field] is not JsonValue value
                || value.GetValueKind() != JsonValueKind.String
                || !IsoDuration.TryParse(value.GetValue<string>(), out var parsed)
            )
                return (
                    null,
                    $"The timer of {State} reads '{field}', which must hold an ISO-8601 duration such as \"PT5M\"."
                );
            read = parsed;
        }
        else
        {
            try
            {
                read = Computed?.Invoke(context);
            }
            catch (Exception)
            {
                read = null;
            }
            if (read is null)
                return (
                    null,
                    $"The timer of {State} could not read its duration from the context."
                );
        }

        var duration = read.Value;
        if (duration < Min || duration > Max)
            return (
                null,
                $"The timer of {State} waits {IsoDuration.Format(duration)}, outside its bounds of "
                    + $"{IsoDuration.Format(Min)} to {IsoDuration.Format(Max)}."
            );
        return (duration, null);
    }
}

/// <summary>
/// What a write of a snapshot does to its row's timer, set by the engine on a snapshot it produces and by the draft
/// service on one it writes. Never part of the snapshot's wire or its equality.
/// </summary>
/// <param name="Duration">How long the state's timer waits, or null when the state has no timer: the due time is cleared.</param>
/// <param name="Restart">
/// Whether the write enters the state (a transition, an outcome, a fired timer, a created row): the due time is set
/// from now. Otherwise a row already in the state keeps the due time it was given at entry.
/// </param>
internal readonly record struct SnapshotTimer(TimeSpan? Duration, bool Restart)
{
    /// <summary>The state has no timer.</summary>
    public static SnapshotTimer None => new(null, false);
}

/// <summary>
/// Names timer triggers. A trigger enum member cannot contain a dot, so <c>&lt;State&gt;.after</c> never collides with
/// a user's trigger, and the TypeScript twin reads the same names from the IR.
/// </summary>
internal static class TimerTriggers
{
    /// <summary>The suffix of every timer trigger.</summary>
    public const string Suffix = "after";

    public static string Name<TState>(TState state)
        where TState : struct, Enum => $"{state}.{Suffix}";

    /// <summary>Splits <c>&lt;State&gt;.after</c>; false for anything else.</summary>
    public static bool TryParse(string trigger, out string state)
    {
        state = "";
        var dot = trigger.LastIndexOf('.');
        if (dot <= 0 || trigger[(dot + 1)..] != Suffix)
            return false;
        state = trigger[..dot];
        return true;
    }
}

/// <summary>
/// ISO-8601 durations as a timer reads and writes them: days, hours, minutes and seconds (<c>P1DT2H30M</c>,
/// <c>PT0.5S</c>), never negative, and with no years, months or weeks, whose length is not fixed. The TypeScript twin
/// parses the same subset, so both engines accept and refuse the same text. The digits are ASCII only: .NET's
/// <c>\d</c> matches every Unicode decimal digit, which neither the twin nor <see cref="decimal"/> reads.
/// </summary>
internal static partial class IsoDuration
{
    [GeneratedRegex(
        @"^P(?!$)(?:([0-9]{1,9})D)?(?:T(?=[0-9])(?:([0-9]{1,9})H)?(?:([0-9]{1,9})M)?(?:([0-9]{1,9})(?:\.([0-9]{1,7}))?S)?)?\z",
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

/// <summary>
/// A <see cref="TimeSpan"/> as JSON: the ISO-8601 duration a timer reads (<c>"PT5M"</c>), never .NET's
/// <c>"00:05:00"</c>. A context field a timer reads its duration from is written and read through this, by the engine
/// and by the typed machine surface alike. Text outside the subset <see cref="IsoDuration"/> parses is a
/// <see cref="JsonException"/>, which a reader answers as invalid input.
/// </summary>
internal sealed class IsoDurationJsonConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader.TokenType == JsonTokenType.String
        && IsoDuration.TryParse(reader.GetString(), out var duration)
            ? duration
            : throw new JsonException("Not an ISO-8601 duration.");

    public override void Write(
        Utf8JsonWriter writer,
        TimeSpan value,
        JsonSerializerOptions options
    ) => writer.WriteStringValue(IsoDuration.Format(value));
}
