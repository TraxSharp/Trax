---
layout: default
title: Test Conventions
parent: Reference
nav_order: 12
---

# Test Conventions

## Folder layout

Every test project follows the same structure:

```
Fixtures/           TestSetup base classes or static factories
Fakes/Trains/       test train implementations and interfaces
Fakes/Models/       test DTOs, inputs, outputs
Utils/              test-only utilities
UnitTests/          no shared DI container or database
IntegrationTests/   shared DI container and/or database
```

`TestFolderLayoutTests` enforces the top-level shape in each repo.

## Fixture patterns

| Pattern | When | Where |
|---|---|---|
| Abstract base class | Integration tests sharing one `ServiceProvider`. `[OneTimeSetUp]` builds it, `[SetUp]` opens a per-test scope. | `Fixtures/TestSetup.cs` |
| Static factory | Tests needing several distinct DI configurations. Exposes `CreateTestServiceProvider()` and friends. | `Fixtures/TestSetup.cs` |
| Inline | Unit tests. A `new ServiceCollection()` per test, no `Fixtures/` folder. | in the test |

## Naming

Test methods are `Method_Scenario_Outcome`, for example
`Build_NoAssemblies_ThrowsInvalidOperation`. Test classes are `{Feature}Tests`. Group related
tests with `#region {FeatureOrMethod}`.

## Assertions

FluentAssertions only. The legacy NUnit forms (`Assert.That`, `Assert.AreEqual` and the rest)
are rejected by `NoLegacyAssertTests`. The reason is the `because` argument: it is where a
failing test explains the rule it was protecting, and the legacy forms have nowhere to put it.

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

```csharp
var ok = await WaitUntilAsync(() => result == expected, TimeSpan.FromSeconds(15));
ok.Should().BeTrue("the service should produce the expected result");
```

A fixed `Task.Delay` is acceptable only when measuring an interval or verifying a negative
that requires the duration to elapse, and a comment must say which. `NoFixedTaskDelayTests`
enforces this, with marker comments (`determinism:`, `allowed-delay:`, `measuring-interval:`,
`negative-wait:`) for the legitimate cases.

## Skipping

`[Ignore]` is not used. A test that cannot run in the current environment calls
`Assert.Ignore("...")` after an explicit reachability check, so the skip and its reason appear
in the run output instead of hiding at declaration time.
