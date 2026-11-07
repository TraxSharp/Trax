using Reqnroll;

namespace Trax.StateMachine.Gherkin.Reqnroll;

/// <summary>
/// The one binding of every step (ADR <c>Trax.Docs/adr/0058</c>): each step is read as derivation reads it and run on
/// the host, so a feature means the same here as in <c>trax machine run</c>. A Given or When the glossary does not read
/// fails; a Then it does not read is about the UI, and is pending.
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

    private async Task Step(StepKind kind, string text)
    {
        var options = TraxFeatures.Options;
        var glossary = options.Glossary.Inner;
        if (!scenario.TryGetValue<Running>(SessionKey, out var running))
        {
            var id =
                $"{feature.FeatureInfo.Title}/{scenario.ScenarioInfo.Title}/{Guid.NewGuid():N}";
            var driver = new HostDriver(options, glossary, id);
            running = new Running(
                new ScenarioSession(glossary, driver),
                new StepResolver(glossary),
                driver
            );
            scenario.Set(running, SessionKey);
        }

        var meaning = running.Resolver.Resolve(
            text,
            running.Session.People,
            out var problem,
            out var unresolved
        );
        if (meaning is null)
        {
            if (unresolved && kind == StepKind.Then)
                throw new PendingStepException($"'{text}' is about the UI: no Trax step reads it.");
            throw new InvalidOperationException(
                problem ?? $"No phrase in the glossary says '{text}'."
            );
        }
        // Reqnroll reports the step's own line; the session needs none.
        if (await running.Session.Step(new FeatureStep(kind, text, 0), meaning) is { } failure)
            throw new FeatureStepFailedException(failure);
    }

    private sealed record Running(
        ScenarioSession Session,
        StepResolver Resolver,
        HostDriver Driver
    );
}

/// <summary>A step whose check did not hold on the host, and why.</summary>
public sealed class FeatureStepFailedException(string message) : Exception(message);
