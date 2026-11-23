using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>How a scenario or a step came out.</summary>
public enum FeatureStatus
{
    /// <summary>It ran and every check held.</summary>
    Passed,

    /// <summary>It ran and a check did not hold.</summary>
    Failed,

    /// <summary>A Given or When derivation cannot read: it did not run.</summary>
    Undefined,

    /// <summary>
    /// A step not run: after a failure, in a dry run, or a step about the UI. A scenario tagged <c>@host</c>, which only
    /// the host bindings run, is skipped by an in-memory run.
    /// </summary>
    Skipped,
}
