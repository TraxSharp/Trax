using System.Text.Json.Nodes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>A person of a scenario: <c>Ada is a customer</c>.</summary>
internal sealed record Person(string Name, GlossaryActor Actor);
