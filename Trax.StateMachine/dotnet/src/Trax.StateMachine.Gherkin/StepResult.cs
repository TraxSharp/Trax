using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>One step's result: <see cref="Message"/> says why it failed, or that it is about the UI and was skipped.</summary>
internal sealed record StepResult(FeatureStep Step, ScenarioStatus? Status, string? Message);
