using System.Globalization;
using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

internal enum GapKind
{
    /// <summary>A machine no scenario uses, so none is derived.</summary>
    Machine,

    /// <summary>A trigger no scenario fires, so it is not served.</summary>
    Trigger,

    /// <summary>A state no scenario reaches.</summary>
    State,

    /// <summary>A guard no scenario uses.</summary>
    Guard,

    /// <summary>A guarded move no scenario shows refused.</summary>
    Refusal,

    /// <summary>An invoked train's outcome no scenario shows.</summary>
    Outcome,

    /// <summary>A view no scenario reads, so it is not served.</summary>
    View,

    /// <summary>An actor a rule admits by its roles that no scenario shows: a grant nobody meant.</summary>
    Grant,
}
