using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Which scenarios a run takes, as Cucumber selects them: a tag expression (<c>@smoke and not @slow</c>), name
/// patterns (any may match), and locations (<c>ordering.feature:12</c>, a scenario's line or an outline row's).
/// </summary>
internal sealed record RunSelection(
    string? Tags = null,
    IReadOnlyList<string>? Names = null,
    IReadOnlyList<(string File, long Line)>? Locations = null
);
