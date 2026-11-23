using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

/// <summary>
/// One field a view shows: a machine's state (<see cref="Source"/> is <c>state</c>), labelled per state if it gives
/// labels, or one of the machine's context fields.
/// </summary>
internal sealed record ViewField(
    string Name,
    string Machine,
    string Source,
    IReadOnlyDictionary<string, string>? Labels
)
{
    internal const string State = "state";

    internal bool IsState => string.Equals(Source, State, StringComparison.Ordinal);
}
