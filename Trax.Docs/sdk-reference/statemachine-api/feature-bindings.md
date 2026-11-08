---
title: Feature bindings
description: "Reference for Trax.StateMachine.Gherkin.Reqnroll: Reqnroll bindings that run Trax features against a host in the test process."
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 20
package: Trax.StateMachine.Gherkin.Reqnroll
namespace: Trax.StateMachine.Gherkin.Reqnroll
source:
  - Trax.StateMachine/dotnet/src/Trax.StateMachine.Gherkin.Reqnroll/TraxFeatures.cs
  - Trax.StateMachine/dotnet/src/Trax.StateMachine.Gherkin.Reqnroll/TraxFeatureSteps.cs
---

# Feature bindings

`Trax.StateMachine.Gherkin.Reqnroll` runs a team's features against a host in the test process with Reqnroll. It
holds one binding for every step: each step is read as [derivation](/docs/sdk-reference/statemachine-api/feature-machines)
reads it, so a feature means the same here as in `trax machine run`. There is no step code to write. The how-to is
[Write machines as features](/docs/statemachine/write-machines-as-features), and why it is so is
[ADR 0058](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0058-features-and-a-glossary-are-a-source-of-machines.md).

## Members

| Member | Description |
| --- | --- |
| `TraxFeatures.Configure(Action<TraxFeatureDriverOptions> configure)` | Sets what the bindings run against. Call it once from a `[BeforeTestRun]` hook, after the host under test has started. Without `Host`, `Client`, `Glossary` and `Credential` it throws an `ArgumentException`. |
| `TraxFeatureDriverOptions` | `Host`, the host's services; `Client`, an `HttpClient` to it (`WebApplicationFactory.CreateClient()`); `GraphQLPath`, `/trax/graphql` by default; `Glossary`, a `FeatureGlossary`; `Credential`, a person's credential; `Slack`, how long past its due time a timer, policy or invoked train may take on the host before a Then is read anyway, five seconds by default. |
| `FeatureActor` | The actor a credential is made for: its `Name`, its `Roles`, and `AnyAuthenticated` for an actor any signed-in caller is. |
| `FeatureCredential` | A person's `Headers`, sent with each of their requests, and the `UserKey` the host's principal reads from them, which their instances are stored under. |
| `TraxFeatureSteps` | The binding. Its `Given`, `When` and `Then` take every step. A Given or When the glossary does not read fails; a Then it does not read is about the UI and is pending. |
| `FeatureStepFailedException` | Thrown by a step whose check did not hold on the host, with the reason. |

## Behavior

- **Each person is a user of their own.** `Credential` is called with the person's actor, their name and an id unique
  to the scenario's run, so a test host mints a token or key per person per scenario and no scenario sees another's
  instances.
- **A Given is arranged, not acted.** It writes the instance through the host's store, as a save, so it fires nothing
  and sets off no policy; the timer and the run of the state it enters start as they would.
- **Everything a person does goes over GraphQL.** A trigger the host does not serve them is not served, and a rule that
  does not admit them is `FORBIDDEN`, as the host says it. A step no person takes fires as the host's server does.
- **Time is waited for exactly.** `5 seconds pass` waits five seconds, then the host gets `Slack` to fire what fell due
  and run the policies it owes. A Then after an invoked train reads the instance's transition history for where the
  train's outcome took it.
- **Reqnroll sends telemetry; the bindings do not.** Reqnroll, which this package brings in, sends usage data when
  it generates a test project's feature code at build time and when a test run starts. Set the environment variable
  `REQNROLL_TELEMETRY_ENABLED` to `0` (any value but `1` turns it off) for the build and the tests, on developer
  machines and in CI.

## Example

```csharp fragment
[Binding]
public sealed class Hooks
{
    [BeforeTestRun]
    public static void Start()
    {
        var app = new WebApplicationFactory<Program>();
        TraxFeatures.Configure(o =>
        {
            o.Host = app.Services;
            o.Client = app.CreateClient();
            o.Glossary = FeatureGlossary.Read(File.ReadAllText("features/glossary.json"));
            o.Credential = (actor, person, scenario) =>
                new(
                    new Dictionary<string, string> { ["Authorization"] = "Bearer " + Tokens.Mint($"{scenario}-{person}", actor.Roles) },
                    $"{scenario}-{person}"
                );
        });
    }
}
```
