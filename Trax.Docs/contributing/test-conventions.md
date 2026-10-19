---
title: Test Conventions
description: "Trax test conventions: layout, fixtures, naming, assertions, determinism, skipping, red tests, dashboard and Postgres query tests, and the IDE extensions."
type: reference
area: contributing
nav_order: 3
redirect_from:
  - /docs/reference/test-conventions
---

# Test Conventions

## Folder layout

These are the folder names to reach for, not a set every project has:

```
Fixtures/           TestSetup base classes or static factories
Fakes/Trains/       test train implementations and interfaces
Fakes/Models/       test DTOs, inputs, outputs
Utils/              test-only utilities
UnitTests/          no shared DI container or database
IntegrationTests/   shared DI container and/or database
```

A project takes the ones it needs. Most have two or three, and `Fakes/Models/` exists in
exactly one project in the repository. Use a name from this list when it fits; invent one only
when nothing here describes what the folder holds.

`TestFolderLayoutTests` does not check that list. It checks three things, all of them shape
rather than content: no folder named `Junk`, `Tmp`, `Temp`, `Misc`, `Old`, `Legacy`, `.vs`,
`.idea` or `node_modules` directly under a test project; every other top-level folder name in
PascalCase; and the `.gitignore` of the project's folder (`Trax.Core/`, `Trax.Effect/`, ...) mentioning
`TestResults`. A project whose only folder is
`Banana/` passes all three.

## Fixture patterns

| Pattern | When | Where |
|---|---|---|
| Abstract base class | Integration tests sharing one `ServiceProvider`. `[OneTimeSetUp]` builds it, `[SetUp]` opens a per-test scope. | `Fixtures/TestSetup.cs` |
| Static factory | Tests needing several distinct DI configurations. A `static class TestSetup` of `Create<Case>Services()` / `Create<Case>ServiceProvider()` methods, one per configuration. | `Fixtures/TestSetup.cs` |
| Inline | Unit tests. A `new ServiceCollection()` per test, no `Fixtures/` folder. | in the test |

## Naming

Test methods are `Method_Scenario_Outcome`, for example
`Build_NoAssemblies_ThrowsInvalidOperation`. Test classes are `{Feature}Tests`. Group related
tests with `#region {FeatureOrMethod}`.

## Assertions

[AwesomeAssertions](https://www.nuget.org/packages/AwesomeAssertions) only (the Apache-2.0 fork of FluentAssertions 7: the same
`.Should()` API, `using AwesomeAssertions;`). The legacy NUnit forms (`Assert.That`, `Assert.AreEqual` and the rest)
are rejected by `NoLegacyAssertTests`. The reason is the `because` argument: it is where a
failing test explains the rule it was protecting, and the legacy forms have nowhere to put it.

## Property tests

A statement that has to hold for every input (ids are unique, the same graph serialises to the
same text) is tested over generated cases with [CsCheck](https://www.nuget.org/packages/CsCheck),
not over a few hand-picked ones:

```csharp fragment
Keys.Sample(keys => Number(keys).Should().OnlyHaveUniqueItems());
```

When a property fails, CsCheck shrinks the input to a small failing case and prints its seed.
Pin that seed as its own test with `Sample(..., seed: "...")` before fixing the code, so the case
keeps running after the generators change.

Only a test project, one that sets `<IsTestProject>true</IsTestProject>`, may reference CsCheck;
a shipped package or a `Trax.*.Testing` package never does. `NoTestFrameworkInSrcTests` fails the
build otherwise, and `Trax.Docs/adr/0044` records why.

## Coverage

A feature is not done until its tests cover the happy path, the boundaries (empty, single,
maximum, zero, off-by-one), null and missing data, the error paths, every valid state
transition and the invalid ones, how the change behaves composed with other parts, and, where
the code runs in parallel, the race conditions.

Coverage is not achieved by tests that exist to raise a percentage. A test that cannot
articulate the regression it would catch should not exist. In particular, an assertion that
passes with broken code is worse than no assertion, because it is counted.

## Determinism

A test that passes locally and fails sometimes in CI is a defect. Synchronise on the
condition that means the work finished, with a generous timeout as a ceiling, and assert the
wait succeeded:

```csharp fragment
var ok = await WaitUntilAsync(() => result == expected, TimeSpan.FromSeconds(15));
ok.Should().BeTrue("the service should produce the expected result");
```

A fixed `Task.Delay` is acceptable only when measuring an interval or verifying a negative
that requires the duration to elapse, and a comment must say which. `NoFixedTaskDelayTests`
enforces this, with marker comments (`determinism:`, `allowed-delay:`, `measuring-interval:`,
`negative-wait:`) for the legitimate cases.

A deadline is only as good as the timeouts underneath it. Set every component timeout that
can delay what the test is waiting for to something shorter than the test's own ceiling, so
a stall is reported as the error that caused it rather than as a cancellation with only the
test helper in the stack:

```csharp fragment
jwt.UseAuthority(jwks.Issuer, Audience)
    .AllowHttpMetadata()
    // The receive below allows 10s; the JwtBearer backchannel default is 60s.
    .CustomizeBearerOptions(o => o.BackchannelTimeout = TimeSpan.FromSeconds(5));
```

Backchannel and connection timeouts, command timeouts, broker acknowledgement windows and
host shutdown timeouts all sit behind ordinary awaits with defaults measured in tens of
seconds. `Trax.Docs/adr/0014` records why this is a rule, and what it costs: a default a
test overrides is a default nothing exercises.

## Skipping

A test that cannot run in the current environment calls `Assert.Ignore("...")` after an
explicit reachability check, so the skip and its reason appear in the run output instead of
hiding at declaration time. `NoIgnoreAttributeTests` rejects new `[Ignore]` attributes.

One survives, on its exceptions list: a Trax.Samples E2E test waiting on an unreleased
scheduler feature. Do not read it as precedent for a second.

When CI provides what the test needs (a service in `.github/ci/packages.json`, node and the
`Trax.Api.StateMachine` packages for `"state-machine": true`), the check fails instead of
skipping when the `CI` environment variable is set, so a broken job cannot report a skipped
suite as green. `PersistedOperationCrossNodeTests` and `TwinSequenceParityTests` do this.

A suite meant to be run by hand carries `[Explicit]` on each concrete fixture, as the Trax.Api and
Trax.Scheduler stress suites do: a normal `dotnet test` skips it and `--filter TestCategory=Stress` runs
it. Put the attribute on the concrete class, because NUnit does not inherit `[Explicit]` or
`[Ignore]` from a base class.

## Tests written before their code

A test written ahead of the code it tests is marked `[Red("what it waits for")]` on each test
method (`using Trax.Testing.Red;`). It runs, and its verdict is inverted: a failure or error is
reported as a pass, with the failure's first line in the message, and a pass is reported as a
failure telling you to remove the attribute, so the marker cannot outlive the gap it records.
It also adds the category `Red`, so `--filter TestCategory=Red` lists what is still pending.
A red test is not a skip: it runs on every build, and `[Red]` never stands in for `[Ignore]`.

The attribute is `tests/RedAttribute.cs` in each .NET folder, linked into every test project
by `tests/Directory.Build.props`. The copies are identical, and `RedAttributeTests` in
Trax.Core checks its behaviour. Remove the attribute, and its `using`, in the change that
makes the test pass.

## Operations console tests

The console's own tests live with its app in `Trax.Api/src/Trax.Api.Dashboard/ClientApp`: vitest
for its modules and pages, and every story run as a test against the mock schema
(`npx vitest run`). The tests that drive it in a real browser, against a host built in-process,
are `Trax.Samples/tests/Trax.Samples.ApiDashboard.Browser`, run in CI as the `console-browser`
suite. Its page objects locate by role and accessible name.

## Operations query tests

`CountEstimatorTests` and `OperationsQueriesTests` in `Trax.Api/tests/Trax.Api.Tests` run against
Postgres, not InMemory, because `CountEstimator` reads the planner's row estimate from
`pg_class.reltuples`. Both use the `trax_api_operations` database on the port `TRAX_TEST_PG_PORT`
names (5432 when unset), and empty the tables they touch in `[SetUp]` with
`TRUNCATE ... RESTART IDENTITY CASCADE`, so each test starts from empty tables and fresh ids.
`CountEstimatorTests` seeds a table past `CountEstimator.EstimateThreshold` and runs `ANALYZE` on
it before asserting, because the estimate is only as fresh as the table's last analysis, and
compares within a tolerance rather than exactly.

## Working on the IDE extensions

The two inlay hint extensions in `Trax.Core/plugins` have no test suite in CI. What each one does
is on [IDE extensions](/docs/core/ide-extensions); how they work, for changing them:

- **VSCode** (`plugins/vscode/trax-hints`). `provider.ts` finds `.Chain<T>()`,
  `.ShortCircuit<T>()` and `.IChain<T>()` calls with a regular expression, asks the editor's
  definition provider (`vscode.executeDefinitionProvider`) where `T` is declared, and reads the
  generic arguments from that class declaration (`typeParser.ts`). Resolved types are cached by
  junction name, and the cache is cleared whenever a `.cs` file is created, changed or deleted.
  When the language server has not answered yet, the hints are refreshed again after five seconds.
  `build.sh` compiles the extension and packages a `.vsix`.
- **Rider and ReSharper** (`plugins/rider-resharper`). `ChainJunctionHintAnalyzer` is an
  `ElementProblemAnalyzer<IInvocationExpression>`: on each `Chain`, `ShortCircuit` or `IChain`
  invocation with a type argument, `PsiJunctionTypeResolver` walks the junction's base types to
  `IJunction<TIn, TOut>` through the PSI, so generics and inheritance are resolved semantically.
  `./gradlew buildPlugin` builds it into `build/distributions/`.

The Roslyn analyzer is a separate thing: inside the Trax.Core solution, `Trax.Core/Directory.Build.props`
adds `Trax.Core.Analyzers` to every other project as an analyzer reference
(`OutputItemType="Analyzer"`, `ReferenceOutputAssembly="false"`), so Trax.Core's own projects are
built with it.
