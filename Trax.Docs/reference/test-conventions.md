---
layout: default
title: Test Conventions
parent: Reference
nav_order: 12
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
exactly one project workspace-wide. Use a name from this list when it fits; invent one only
when nothing here describes what the folder holds.

`TestFolderLayoutTests` does not check that list. It checks three things, all of them shape
rather than content: no folder named `Junk`, `Tmp`, `Temp`, `Misc`, `Old`, `Legacy`, `.vs`,
`.idea` or `node_modules` directly under a test project; every other top-level folder name in
PascalCase; and the repo `.gitignore` mentioning `TestResults`. A project whose only folder is
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

A deadline is only as good as the timeouts underneath it. Set every component timeout that
can delay what the test is waiting for to something shorter than the test's own ceiling, so
a stall is reported as the error that caused it rather than as a cancellation with only the
test helper in the stack:

```csharp
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

Three survive, each on its exceptions list: the Trax.Api and Trax.Scheduler stress fixtures,
which gate suites meant to be run by hand, and one Trax.Samples E2E test waiting on an
unreleased scheduler feature. Do not read them as precedent for a fourth.
