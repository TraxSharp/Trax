using System.Text.RegularExpressions;

namespace Trax.StateMachine.Gherkin.Reqnroll;

/// <summary>A step a test project registered, as <see cref="TraxFeatureDriverOptions.Given"/> took it.</summary>
internal sealed record RegisteredStep(
    StepKind Kind,
    string Pattern,
    Regex Regex,
    Func<FeatureScenario, Match, Task> Run
);
