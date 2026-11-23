using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Trax.StateMachine;

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
        if (dot <= 0 || !string.Equals(trigger[(dot + 1)..], Suffix, StringComparison.Ordinal))
            return false;
        state = trigger[..dot];
        return true;
    }
}
