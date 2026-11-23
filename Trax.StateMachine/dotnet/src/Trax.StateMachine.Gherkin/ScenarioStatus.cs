using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

internal enum ScenarioStatus
{
    Passed,
    Failed,

    /// <summary>A Given or When derivation cannot read: the scenario was not run.</summary>
    Undefined,

    /// <summary>A scenario tagged <c>@host</c>, which only the host bindings run.</summary>
    Skipped,
}
