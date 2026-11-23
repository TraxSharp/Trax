using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>
/// Something the glossary names that no scenario shows, and a scenario to paste that shows it, checked green first, or
/// null when none can be written. <see cref="Kind"/> is <c>machine</c>, <c>trigger</c>, <c>state</c>, <c>guard</c>,
/// <c>refusal</c>, <c>outcome</c>, <c>view</c> or <c>grant</c>.
/// </summary>
public sealed record FeatureGap(
    string Kind,
    string Machine,
    string What,
    string Message,
    string? Stub
);
