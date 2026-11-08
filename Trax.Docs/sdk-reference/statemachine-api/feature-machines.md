---
title: Feature machines
description: "Reference for Trax.StateMachine.Gherkin: derive machines from features and a glossary, run the features in memory, list gaps, draw the board."
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 19
package: Trax.StateMachine.Gherkin
namespace: Trax.StateMachine.Gherkin
source:
  - Trax.StateMachine/dotnet/src/Trax.StateMachine.Gherkin/FeatureMachines.cs
---

# Feature machines

`Trax.StateMachine.Gherkin` derives state machines from Gherkin features and a glossary: each machine's IR, the host
file that says who may use it, and the access report. `trax machine from-gherkin`, `run`, `missing` and `diagram` are
these calls, and a host serves what they write with
[`ServeFeatures`](/docs/sdk-reference/statemachine-api/add-state-machines). The files are in the
[feature files reference](/docs/reference/feature-files), the how-to is
[Write machines as features](/docs/statemachine/write-machines-as-features), and why it is so is
[ADR 0058](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0058-features-and-a-glossary-are-a-source-of-machines.md).

## Members

| Member | Description |
| --- | --- |
| `FeatureGlossary.Read(string json)` | Reads a glossary. A text that is not one throws a `FormatException` naming the key. |
| `FeatureFile` | One feature file: its `Path`, as provenance and every problem names it, and its `Text`. |
| `FeatureMachines.Derive(glossary, features)` | Derives every machine the features use, then replays every scenario on what it derived, access included. Returns `DerivedMachines`. |
| `DerivedMachines` | `Irs`, one IR per machine by id; `Host`, the host file; `Access`, the access report; `Problems`, what stopped it, each a `FeatureProblem`; `UiSteps`, the Thens about the UI that derivation does not read. `Succeeded` is true when there is no problem, and only then are the files set. |
| `FeatureProblem` | A `File`, a `Line` and a `Message`. |
| `FeatureMachines.Run(glossary, features, irs, host, options)` | Runs the features in memory on machines, given as their IRs by id and their host file. Each answer is the host's, access included: a trigger the host does not serve the caller is not served, another owner's instance is `NOT_FOUND`, and a caller the rule does not admit is `FORBIDDEN`. Each scenario runs on fresh instances. The IRs and the host file are checked as a host checks them before any scenario runs: an IR that is not JSON, a machine of one missing from the other, a name the host file gives that its IR lacks, or an IR the host file was not derived from throws a `FormatException`, as does a tag expression that is not one; a name pattern that is not a regular expression throws an `ArgumentException`. An overload takes the `DerivedMachines` of `Derive` and throws an `ArgumentException` when it did not succeed. Returns `FeatureRunReport`. |
| `FeatureRunOptions` | `Tags`, a Cucumber tag expression (`@smoke and not @slow`); `Names`, regular expressions any of which a scenario's name matches; `Locations`, file and line pairs; `DryRun`, read every step and run none; `FailFast`, stop at the first scenario that does not pass. |
| `FeatureRunReport` | `Scenarios`, in file and line order, each a `FeatureScenarioResult`, and `Problems`, files that could not be read. `Passed` is true when there is no problem and every scenario passed. |
| `FeatureScenarioResult` | A scenario's `File`, `Line`, `Name`, `Tags`, `Status` and `Steps`, each a `FeatureStepResult` with its `Line`, `Keyword`, `Text`, `Status` and `Message`, why it failed or was skipped. |
| `FeatureStatus` | `Passed`, `Failed`, `Undefined` (a Given or When derivation cannot read) and `Skipped` (after a failure, in a dry run, or a step about the UI). |
| `FeatureMachines.Missing(glossary, features)` | What the glossary names and no scenario shows, each a `FeatureGap`: its `Kind` (`machine`, `trigger`, `state`, `guard`, `refusal`, `outcome`, `view` or `grant`), the `Machine`, `What` it is, a `Message`, and a `Stub`, a scenario to paste, or null when none can be written. A stub is offered only if the features with it pasted derive with no problem and change only what it claims. |
| `FeatureMachines.Board(irs, host)` | The event-storming board, from the IRs and the host file alone: a `FeatureBoard` of `Stickies` and `Links`. A `FeatureSticky` has an `Id`, a `Kind` (`actor`, `command`, `event`, `policy`, `aggregate` or `view`), its `Machine`, a `Label`, and the `Scenarios` behind it, each a `FeatureScenarioRef` (`File`, `Line`, `Scenario`) when the host file has provenance. A `FeatureLink` goes `From` one sticky's id `To` another's. The files are checked as `Run` checks them, and refused with the same `FormatException`. |

## Example

```csharp fragment
var glossary = FeatureGlossary.Read(File.ReadAllText("features/glossary.json"));
FeatureFile[] features = [new("ordering.feature", File.ReadAllText("features/ordering.feature"))];

var derived = FeatureMachines.Derive(glossary, features);
if (!derived.Succeeded)
    foreach (var problem in derived.Problems)
        Console.Error.WriteLine(problem);

var report = FeatureMachines.Run(glossary, features, derived, new FeatureRunOptions { Tags = "@smoke" });
```
