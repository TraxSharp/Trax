using Gherkin;
using Gherkin.CucumberMessages;
using Gherkin.CucumberMessages.Pickles;
using Io.Cucumber.Messages.Types;

namespace Trax.StateMachine.Gherkin;

/// <summary>One step of a scenario, at the line it is written on.</summary>
internal sealed record FeatureStep(StepKind Kind, string Text, long Line);
