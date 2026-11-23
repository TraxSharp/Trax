using Reqnroll;

namespace Trax.StateMachine.Gherkin.Reqnroll;

/// <summary>
/// The one binding of every step (ADR <c>Trax.Docs/adr/0058</c>): each step is read as derivation reads it and run on
/// the host, so a feature means the same here as in <c>trax machine run</c>. A step the glossary does not read runs the
/// test project's own step for it (<see cref="TraxFeatureDriverOptions.Given"/>), if one is registered. A Given or When
/// neither reads fails. A Then neither reads is about the UI, and is pending in a machine feature, a feature file under a
/// folder named <c>features</c>, subfolders included, as derivation reads them; in any other feature, a spec under
/// <c>specs/</c> among them, it fails, so a mistyped check never passes as pending. A feature under both is refused.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/feature-bindings">SDK reference</seealso>
[Binding]
public sealed class TraxFeatureSteps(ScenarioContext scenario, FeatureContext feature)
{
    private const string SessionKey = "Trax.FeatureSession";

    /// <summary>A Given, an And or But after one.</summary>
    [Given(@"^(.*)$")]
    public Task Given(string text) => Step(StepKind.Given, text);

    /// <summary>A When, an And or But after one.</summary>
    [When(@"^(.*)$")]
    public Task When(string text) => Step(StepKind.When, text);

    /// <summary>A Then, an And or But after one.</summary>
    [Then(@"^(.*)$")]
    public Task Then(string text) => Step(StepKind.Then, text);

    private Task Step(StepKind kind, string text)
    {
        if (!scenario.TryGetValue<HostFeatureRun>(SessionKey, out var run))
        {
            run = new HostFeatureRun(
                TraxFeatures.Options,
                $"{feature.FeatureInfo.Title}/{scenario.ScenarioInfo.Title}/{Guid.NewGuid():N}",
                HostFeatureRun.IsMachineFeature(feature.FeatureInfo.FolderPath)
            );
            scenario.Set(run, SessionKey);
        }
        return run.Step(kind, text);
    }
}
