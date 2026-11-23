using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Trax.StateMachine;

/// <summary>The limits every state's timer is held to.</summary>
public static class MachineTimers
{
    /// <summary>
    /// The longest a timer may wait: 366 days. A fixed duration above it, or a context duration whose declared maximum
    /// is above it, refuses to build. A timer that waits longer than this is a schedule, not a step of a machine.
    /// </summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(366);

    /// <summary>
    /// Why the timer of <paramref name="state"/> may not wait <paramref name="wait"/>, a fixed duration above
    /// <see cref="MaxDuration"/>: the builder's words, which derivation from features repeats.
    /// </summary>
    internal static string TooLong(string state, TimeSpan wait) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"the timer of {state} waits {wait}, longer than the {MaxDuration.TotalDays} days a timer may wait "
        ) + "(MachineTimers.MaxDuration).";
}
