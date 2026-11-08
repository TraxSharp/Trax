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
  - Trax.StateMachine/dotnet/src/Trax.StateMachine.Gherkin.Reqnroll/FeatureScenario.cs
---

# Feature bindings

`Trax.StateMachine.Gherkin.Reqnroll` runs a team's features against a host in the test process with Reqnroll. It
holds one binding for every step: each step is read as [derivation](/docs/sdk-reference/statemachine-api/feature-machines)
reads it, so a feature means the same here as in `trax machine run`. A machine feature needs no step code. A spec,
a feature about what the machines do not cover, registers the few steps the glossary does not read. The how-to is
[Write machines as features](/docs/statemachine/write-machines-as-features), and why it is so is
[ADR 0058](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0058-features-and-a-glossary-are-a-source-of-machines.md).

## Members

| Member | Description |
| --- | --- |
| `TraxFeatures.Configure(Action<TraxFeatureDriverOptions> configure)` | Sets what the bindings run against. Call it once from a `[BeforeTestRun]` hook, after the host under test has started. Without `Host`, `Client`, `Glossary` and `Credential` it throws an `ArgumentException`. |
| `TraxFeatureDriverOptions` | `Host`, the host's services; `Client`, an `HttpClient` to it (`WebApplicationFactory.CreateClient()`); `GraphQLPath`, `/trax/graphql` by default; `Glossary`, a `FeatureGlossary`; `Credential`, a person's credential; `Slack`, how long past its due time a timer, policy or invoked train may take on the host before a Then is read anyway, five seconds by default; `Manifest`, the host's `ClientManifest`, to send every operation by its persisted id. |
| `TraxFeatureDriverOptions.Given/When/Then(string pattern, Func<FeatureScenario, Match, Task> step)` | Registers a step of the test project's own. `pattern` is a regular expression matched against the whole step text, its groups in the `Match`. Returns the options, so calls chain. A malformed pattern, or one registered twice for the same keyword, throws an `ArgumentException`. |
| `FeatureScenario` | The scenario a registered step runs in. `Services`, the host's services; `Credential(person)`, the credential the glossary's steps send for that person; `SendAsync(person, request)`, a request sent on `Client` with it; `InstanceOf(person, machine)`, the id of the person's instance of a machine (its glossary id), or null; `Items`, a bag the scenario's steps share. A person the scenario has not introduced throws an `ArgumentException`. |
| `FeatureActor` | The actor a credential is made for: its `Name`, its `Roles`, and `AnyAuthenticated` for an actor any signed-in caller is. |
| `FeatureCredential` | A person's `Headers`, sent with each of their requests. The host's principal reads the user their instances are stored under from them. |
| `TraxFeatureSteps` | The binding. Its `Given`, `When` and `Then` take every step: the glossary reads it first, then a registered step. A Given or When neither reads fails. A Then neither reads is pending in a machine feature and fails in a spec. |
| `FeatureStepFailedException` | Thrown by a step whose check did not hold on the host, with the reason. |

## Behavior

- **Each person is a user of their own.** `Credential` is called with the person's actor, their name and an id unique
  to the scenario's run, so a test host mints a token or key per person per scenario and no scenario sees another's
  instances.
- **A Given is arranged, not acted.** It writes the instance through the host's store, as a save, so it fires nothing
  and sets off no policy; the timer and the run of the state it enters start as they would.
- **Everything a person does goes over GraphQL.** A trigger the host does not serve them is not served, and a rule that
  does not admit them is `FORBIDDEN`, as the host says it. A step no person takes fires as the host's server does.
- **By persisted id, when the host requires it.** With `Manifest` set (the file `--trax-export-client` writes, the one
  a page's generated client is built from), each start, trigger and view read is sent as the id the manifest lists for
  it, with its values as variables, as the page sends it, so the features run against the host's shipped posture. An
  operation the manifest has no document for is not served. Without it, each is sent as a hand-written document. A
  request the host refuses before running it (`PERSISTED_OPERATION_REQUIRED`, `CLIENT_OUTDATED`, `SERVER_OUTDATED`, an
  id it does not hold) fails the step rather than reading as a denial, and a manifest exported from another version of
  a machine than the host serves fails it too: export it again.
- **The glossary reads a step first.** A registered step runs only when no glossary phrase matches the text, so it
  never changes what a glossary step means, and a step two registered patterns match fails as ambiguous. Register
  steps through `TraxFeatures.Configure`, never with Reqnroll's own `[Given]`, `[When]` or `[Then]`: the binding matches
  every step, so Reqnroll would report both as ambiguous. A registered step speaks of people by name, as the glossary
  does; a step that says "I" fails. A glossary Then after a registered When checks the last step the glossary ran.
- **A spec fails where a machine feature is pending.** A machine feature is one whose file is in a folder named
  `features`, any case, the folder derivation reads. The folder is the file's own, as Reqnroll's
  `FeatureInfo.FolderPath` gives it; a `Link` on the item does not change it. Every other feature is a spec: put a
  sample's in `specs/`, beside `features/`. In a machine feature a Then nothing reads is about the UI and is pending;
  in a spec it fails, so a mistyped check never passes.
- **Time is waited for exactly.** `5 seconds pass` waits five seconds, then the host gets `Slack` to fire what fell due
  and run the policies it owes. One step waits at most ten minutes; a step that says more is refused, so run a
  scenario with longer waits in memory (`trax machine run`). A Then after an invoked train reads the instance's transition history for where the
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
            o.Manifest = ClientManifest.Read(File.ReadAllText("client/manifest.json"));
            o.Credential = (actor, person, scenario) =>
                new(new Dictionary<string, string> { ["Authorization"] = "Bearer " + Tokens.Mint($"{scenario}-{person}", actor.Roles) });
            // A spec's own step: Ada's charges, read as Ada.
            o.Then(@"(?<person>[A-Z][a-z]+)'s charges list (?<n>\d+) charges?", async (scenario, match) =>
            {
                var person = match.Groups["person"].Value;
                using var response = await scenario.SendAsync(person, new HttpRequestMessage(HttpMethod.Get, "/charges"));
                var charges = await response.Content.ReadFromJsonAsync<Charge[]>();
                Assert.That(charges, Has.Length.EqualTo(int.Parse(match.Groups["n"].Value)));
            });
        });
    }
}
```
