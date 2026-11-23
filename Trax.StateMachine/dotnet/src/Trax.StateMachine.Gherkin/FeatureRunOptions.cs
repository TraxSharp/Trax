using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Which scenarios a run takes, as Cucumber selects them, and how: a tag expression (<c>@smoke and not @slow</c>),
/// name patterns (any may match), locations (<c>ordering.feature:12</c>), a dry run, and stopping at the first failure.
/// </summary>
public sealed record FeatureRunOptions
{
    /// <summary>A tag expression, or null for every scenario.</summary>
    public string? Tags { get; init; }

    /// <summary>Regular expressions a scenario's name may match, any of them; empty for every name.</summary>
    public IReadOnlyList<string> Names { get; init; } = [];

    /// <summary>Scenarios by file and line (a scenario's, or an example row's); empty for every scenario.</summary>
    public IReadOnlyList<(string File, long Line)> Locations { get; init; } = [];

    /// <summary>Read every step and run none.</summary>
    public bool DryRun { get; init; }

    /// <summary>Stop at the first scenario that does not pass.</summary>
    public bool FailFast { get; init; }
}
