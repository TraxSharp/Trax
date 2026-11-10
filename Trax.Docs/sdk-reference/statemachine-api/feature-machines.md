---
title: Feature machines
description: "Reference for Trax.StateMachine.Gherkin: read a lexicon, derive machines from features, run the features in memory, list gaps, draw the board."
type: reference
area: machines
parent: State Machine API
grand_parent: SDK Reference
nav_order: 19
package: Trax.StateMachine.Gherkin
namespace: Trax.StateMachine.Gherkin
source:
  - Trax.StateMachine/dotnet/src/Trax.StateMachine.Gherkin/FeatureMachines.cs
  - Trax.StateMachine/dotnet/src/Trax.StateMachine.Gherkin/Lexicon.cs
---

# Feature machines

`Trax.StateMachine.Gherkin` reads a features directory's [lexicon](/docs/reference/lexicon) into its glossary, and
derives state machines from Gherkin features and that glossary: each machine's IR, the host file that says who may use
it, and the access report. `trax machine from-gherkin`, `run`, `missing` and `diagram` are
these calls (`trax machine usage` and `explain` are the CLI's own, with no call here), and a host serves what they write with
[`ServeFeatures`](/docs/sdk-reference/statemachine-api/add-state-machines). The files are in the
[feature files reference](/docs/reference/feature-files), the how-to is
[Write machines as features](/docs/statemachine/write-machines-as-features), and why it is so is
[ADR 0058](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0058-features-and-a-glossary-are-a-source-of-machines.md).

## Members

| Member | Description |
| --- | --- |
| `Lexicon.ReadDirectory(directory, trains)` | Reads the one lexicon in a features directory and returns its `LexiconReading`, or null when the directory has none. Two lexicons in one directory are a problem in the reading. See [Lexicon](#lexicon). |
| `Lexicon.Read(path, text)`, `Lexicon.Read(path, text, trains)` | Reads a lexicon's text; `path` is what every problem names. Returns `LexiconReading`. |
| `LexiconReading` | `Glossary`, the `FeatureGlossary` the lexicon elaborates to; `Json`, that glossary's canonical JSON, the `glossary.json` `trax machine from-gherkin` writes; `Problems`, each a `FeatureProblem` at its lexicon line. `Succeeded` is true when there is a glossary, and only then are `Glossary` and `Json` set. `GlossaryOrThrow()` returns the glossary, or throws a `FormatException` listing each problem at its line. |
| `FeatureGlossary.Read(string json)` | Reads a glossary's JSON, for features not yet written with a lexicon. A text that is not one throws a `FormatException` naming the key. |
| `FeatureFile` | One feature file: its `Path`, as provenance and every problem names it, and its `Text`. |
| `FeatureMachines.Derive(glossary, features)` | Derives every machine the features use, then replays every scenario on what it derived, access included. Returns `DerivedMachines`. |
| `DerivedMachines` | `Irs`, one IR per machine by id; `Host`, the host file; `Access`, the access report; `Problems`, what stopped it, each a `FeatureProblem`; `UiSteps`, the Thens about the UI that derivation does not read; `Warnings`, designs that derive and may still not hold on the host, such as a policy that waits for another policy's result, each a `FeatureProblem`. A warning stops nothing. `Succeeded` is true when there is no problem, and only then are the files set. |
| `FeatureProblem` | A `File`, a `Line` and a `Message`. |
| `FeatureMachines.Run(glossary, features, irs, host, options)` | Runs the features in memory on machines, given as their IRs by id and their host file. Each answer is the host's, access included: a trigger the host does not serve the caller is not served, another owner's instance is `NOT_FOUND`, and a caller the rule does not admit is `FORBIDDEN`. Each scenario runs on fresh instances. The IRs and the host file are checked as a host checks them before any scenario runs: an IR that is not JSON, a machine of one missing from the other, a name the host file gives that its IR lacks, or an IR the host file was not derived from throws a `FormatException`, as does a tag expression that is not one; a name pattern that is not a regular expression throws an `ArgumentException`. An overload takes the `DerivedMachines` of `Derive` and throws an `ArgumentException` when it did not succeed. Returns `FeatureRunReport`. |
| `FeatureRunOptions` | `Tags`, a Cucumber tag expression (`@smoke and not @slow`); `Names`, regular expressions any of which a scenario's name matches; `Locations`, file and line pairs; `DryRun`, read every step and run none; `FailFast`, stop at the first scenario that does not pass. |
| `FeatureRunReport` | `Scenarios`, in file and line order, each a `FeatureScenarioResult`, and `Problems`, files that could not be read. `Passed` is true when there is no problem and every scenario passed, or was skipped as one only the host runs (`@host`). |
| `FeatureScenarioResult` | A scenario's `File`, `Line`, `Name`, `Tags`, `Status` and `Steps`, each a `FeatureStepResult` with its `Line`, `Keyword`, `Text`, `Status` and `Message`, why it failed or was skipped. |
| `FeatureStatus` | `Passed`, `Failed`, `Undefined` (a Given or When derivation cannot read) and `Skipped` (after a failure, in a dry run, a step about the UI, or a scenario tagged `@host`). |
| `FeatureMachines.Missing(glossary, features)` | What the glossary names and no scenario shows, each a `FeatureGap`: its `Kind` (`machine`, `trigger`, `state`, `guard`, `refusal`, `outcome`, `view` or `grant`), the `Machine`, `What` it is, a `Message`, and a `Stub`, a scenario to paste, or null when none can be written. A stub is offered only if the features with it pasted derive with no problem and change only what it claims. |
| `FeatureMachines.Board(irs, host)` | The event-storming board, from the IRs and the host file alone: a `FeatureBoard` of `Stickies` and `Links`. A `FeatureSticky` has an `Id`, a `Kind` (`actor`, `command`, `event`, `policy`, `aggregate` or `view`), its `Machine`, a `Label`, and the `Scenarios` behind it, each a `FeatureScenarioRef` (`File`, `Line`, `Scenario`) when the host file has provenance. A `FeatureLink` goes `From` one sticky's id `To` another's. The files are checked as `Run` checks them, and refused with the same `FormatException`. |

## Lexicon

`Lexicon` reads the file people write to say what their features' words mean
([the lexicon reference](/docs/reference/lexicon)) and elaborates it to the glossary the calls below take.

| Member | Description |
| --- | --- |
| `Extension` | `".lexicon"`. |
| `Read(string path, string text)` | Reads a lexicon. Each train's output fields come from its `Returns:` line, and its input fields from its `Takes:` and `Input:` lines. |
| `Read(string path, string text, IEnumerable<Assembly> trains)` | Reads a lexicon, finding each train it runs in `trains` by its full name and reading its input and output records as the host does. `Returns:` and `Takes:` lines are then optional, and one that disagrees with the record is a problem at its line. |
| `ReadDirectory(string directory, IEnumerable<Assembly>? trains = null)` | The one `*.lexicon` in `directory`, read, with the trains when they are given; null when there is none. A second lexicon is a problem. |

Nothing is read in part: a lexicon with any problem gives no glossary, and every problem, including those the glossary
reader finds in what the lexicon elaborated to, names the lexicon line that wrote the key.

## The C# a host binds

A lexicon names the C# behind a machine by its full name, and derivation writes those names to the host file, never
to the IR (see [the feature files reference](/docs/reference/feature-files#the-c-a-machine-names)). A host serving the
directory finds each type in the assemblies given to `ServeFeatures`, as it finds the trains, and refuses the host
when one is missing, of the wrong kind, or does not pair one for one with what the IR names. The three handler kinds
are interfaces of `Trax.StateMachine`, each created once with its public parameterless constructor, so each answers
from its arguments alone:

| Interface | Member | Bound as |
| --- | --- | --- |
| `ICustomRule` | `bool Holds(JsonObject context, JsonNode? input)` | the handler of a custom rule, as `CustomGuard` binds one |
| `ICustomReduction` | `JsonObject Reduce(JsonObject context, JsonNode? input)` | the handler of a custom reduction, as `CustomReducer` binds one |
| `IMachineMigration` | `MigrationResult Migrate(string state, JsonObject context)` | the step from one version to the next, as `MigrateFrom` registers one |

A run-once effect is an interface extending `ISnapshotEffect`, resolved from the host's services when a send runs it,
as `RunsOnce<TEffect>` binds one.

## Example

```csharp fragment
var lexicon = Lexicon.ReadDirectory("features", [typeof(ITakePaymentTrain).Assembly])!;
var glossary = lexicon.GlossaryOrThrow();
FeatureFile[] features = [new("ordering.feature", File.ReadAllText("features/ordering.feature"))];

var derived = FeatureMachines.Derive(glossary, features);
if (!derived.Succeeded)
    foreach (var problem in derived.Problems)
        Console.Error.WriteLine(problem);

var report = FeatureMachines.Run(glossary, features, derived, new FeatureRunOptions { Tags = "@smoke" });
```
