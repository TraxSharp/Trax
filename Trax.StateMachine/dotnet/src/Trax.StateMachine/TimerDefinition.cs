using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Trax.StateMachine;

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
