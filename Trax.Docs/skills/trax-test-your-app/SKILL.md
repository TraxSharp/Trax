---
name: trax-test-your-app
description: Use when writing or fixing tests for a .NET app built on Trax, or adding a test project to one. Covers unit-testing a junction, testing a train through ITrainBus with UseInMemory, testing a deciding train with ScriptedDecider, checking a state machine with MachineConformance, end-to-end host tests with WebApplicationFactory on Postgres, testing scheduler retries and dead letters, what the template test projects already do, and adopting the architecture guards (Trax.Core.Testing, Trax.Mediator.Testing, Trax.Effect.Data.Testing, Trax.Api.GraphQL.Testing, TrainGuardFixture, HygieneGuardFixture, TraxInvariants) so project rules fail a test.
metadata:
  internal: true
---

# Test an app built on Trax

Trax apps are tested at four levels, each cheaper than the next: a junction on its own, a train
through the train bus on the in-memory provider, the whole host started in the test process, and
architecture guards that check the project's shape. Pick the cheapest level that can fail for the
reason you care about. [Unit-test a junction](https://traxsharp.net/docs/cross-cutting/testing)
starts the testing pages, and [2. Testing PlaceOrder](https://traxsharp.net/docs/book/testing-place-order)
builds a test project step by step.

## Decide first

| You are testing | Level | Page |
|---|---|---|
| What one junction returns or throws | Construct it, call `Run`. No container, no database. | [Unit-test a junction](https://traxsharp.net/docs/cross-cutting/testing) |
| A train's chain, its output, its failure, its run record | A `ServiceCollection` with the app's `AddTrax` registration on `UseInMemory()`, run through `ITrainBus` | [Test a train with in-memory effects](https://traxsharp.net/docs/cross-cutting/test-a-train) |
| Each track of a train that asks a decider | `ScriptedDecider` (`Trax.Core.Decisions`), plus `ChainVerification.Verify` over `DeclaredChain()` | [Test a train's decisions](https://traxsharp.net/docs/core/test-train-decisions) |
| A machine's definition against its committed files | `MachineConformance.Check` (`Trax.StateMachine.Testing`) | [Trax.StateMachine.Testing](https://traxsharp.net/docs/sdk-reference/testing/effect-statemachine-testing) |
| A machine's rules, ownership and refusals as a caller sees them | The host over GraphQL | [12.9 Test it](https://traxsharp.net/docs/book/checkout-is-a-state-machine/test-it) |
| Startup, auth, the Production/Development split, GraphQL, the scheduler | `WebApplicationFactory<Program>` | [Test a host end to end](https://traxsharp.net/docs/cross-cutting/e2e-testing) |
| Folder and type rules across the project | Guard fixtures, below | [Adopt the architecture guards](https://traxsharp.net/docs/reference/adopt-the-architecture-guards) |

Then pick the data provider by what the code under test runs. `UseInMemory()` runs no SQL and
has no job dispatcher, no retry delay and no reapers: a test of raw SQL, Postgres locking,
queued runs, retries or dead letters on it proves nothing. Use Postgres there, on a database the
suite owns ([Give the suite its own database](https://traxsharp.net/docs/cross-cutting/e2e-testing#give-the-suite-its-own-database)).

## What the template tests already do

Every template (`trax-api`, `trax-hub`, `trax-scheduler`) generates an NUnit project under
`tests/` that references the app. Keep it and add to it; its files are the pattern.

| File | Shows |
|---|---|
| `UnitTests/FetchDataJunctionTests` (not in `trax-scheduler`) | A junction built with `NullLogger<T>.Instance` and called directly |
| `IntegrationTests/HelloWorldTrainTests` | A train run through `ITrainBus` on `UseInMemory().SaveTrainParameters()`, then its `Metadata` row found by a unique input value |
| `IntegrationTests/HostTests` | The host started in Development and in Production; the demo key works only in Development, and the console is a 404 in Production |

`dotnet test` at the template's root finds only the app project. Name the test project:
`dotnet test tests/MyApp.Tests`. The template test project references no Trax testing package;
add the ones the guards below need.

## Steps for a new train

1. Unit-test each junction that holds logic. Pass fakes for its interfaces; assert the exception
   for each refusal, since a throw is how the train fails.
2. Add one train test per track: build the container in `[SetUp]` and dispose it in
   `[TearDown]`, so each test gets an empty store. `RunAsync<TOut>` returns the output, or throws
   what the failing junction threw.
3. If the train asks a decider, script each answer, and add the `ChainVerification` test so every
   declared track is checked, not only the ones a script took.
4. If it is exposed over GraphQL, add a host test for the caller's view: who may call it, and the
   error code a refusal carries.

## Adopt the architecture guards

Each guard package ships abstract NUnit fixtures with the tests written. Subclass one, keep
`[TestFixture]` on the subclass, supply what it asks for:

```csharp compile=tests
using System.Reflection;
using MyApp.Trains.PlaceOrder;
using NUnit.Framework;
using Trax.Core.Testing.Fixtures;
using Trax.Mediator.Testing;

namespace MyApp.Tests;

[TestFixture]
public sealed class MyAppTrainGuards : TrainGuardFixture
{
    protected override IReadOnlyList<Assembly> TrainAssemblies => [typeof(IPlaceOrderTrain).Assembly];
}

[TestFixture]
public sealed class MyAppHygieneGuards : HygieneGuardFixture;
```

| Package | Fixture | Fails when |
|---|---|---|
| `Trax.Mediator.Testing` | `TrainGuardFixture` | A train has no train interface of its own, or two unrelated ones |
| `Trax.Core.Testing` | `HygieneGuardFixture` | A test under `tests/` uses `[Ignore]`, a fixed `Task.Delay` or `Thread.Sleep`, NUnit's classic asserts including `Assert.That`, or checks a substitute right after `StartAsync` without waiting |
| `Trax.Effect.Data.Testing` | `DomainDataLayerGuardFixture` | A domain data context does not derive `DomainDataContext`, has no companion interface or shares a schema; a migration context has pending model changes; a per-user entity has no owner filter, or query code switches it off (the owner-scope census) |
| `Trax.Api.GraphQL.Testing` | `CrossSchemaGuardFixture` | A cross-schema edge is invalid, does not batch, or a `[Parent]` resolver does not declare what it reads |

Things that trip a first run:

- **No solution file.** The source-scanning guards find the repository root by walking up from
  the test assembly to a folder holding a `*.slnx`, and throw without one. The templates ship
  none: `dotnet new sln`, then `dotnet sln add` both projects.
- **`HygieneGuardFixture` fails the template's own tests**, which assert with `Assert.That`. Move
  them to a fluent library (Trax uses AwesomeAssertions), or leave that fixture out.
- **A guard that inspected nothing fails.** Wrong `SourceScanRoots` or `TrainAssemblies` show up as
  "inspected no files" or "found no train"; the roots are relative to the folder with the `*.slnx`.
- **`RepoConventionGuardFixture` is not for an app.** It expects `Directory.Build.props` to pin the
  version `1.99.99`, Trax's own local-development placeholder. Skip it, or set
  `ExpectedDirectoryBuildPropsVersion` to your own.

### What no guard checks

No guard checks the layout from
[Organize trains in a project](https://traxsharp.net/docs/core/organize-trains): not the folder a
file is in, not that junctions are `internal`, not that each train has its own input type. To make
those fail a test, write the check yourself. A reflection test over the app's assembly covers the
two most often broken:

```csharp compile=tests
using AwesomeAssertions;
using MyApp.Trains.PlaceOrder;
using NUnit.Framework;
using Trax.Core.Junction;

namespace MyApp.Tests;

[TestFixture]
public class LayoutTests
{
    private static readonly Type[] Junctions = typeof(IPlaceOrderTrain)
        .Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false })
        .Where(t =>
            t.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IJunction<,>))
        )
        .ToArray();

    [Test]
    public void Junctions_AreInternal() =>
        Junctions.Where(t => t.IsVisible).Should().BeEmpty("a train's junctions are its own");

    [Test]
    public void Junctions_LiveInAJunctionsFolder() =>
        Junctions
            .Where(t => t.Namespace?.EndsWith(".Junctions", StringComparison.Ordinal) != true)
            .Should()
            .BeEmpty("each train keeps its junctions in Trains/<Train>/Junctions/");
}
```

Internal junctions need `<InternalsVisibleTo Include="MyApp.Tests" />` in the app's csproj for the
unit tests to construct them. The template's sample junctions are `public`; making them `internal`
as the layout page says is part of adopting this test.

The data-layer guards also cannot see whether an owner filter compares the right column. For each
per-user entity, add a cross-user test against the running API, as
[Configure the owner-scope census](https://traxsharp.net/docs/reference/adopt-the-architecture-guards#configure-the-owner-scope-census)
describes.

## Known mistakes

- **Asserting a scheduler behaviour on InMemory.** Retries, backoff and dead letters need Postgres;
  see [Test a cron manifest's retries](https://traxsharp.net/docs/cross-cutting/e2e-testing#test-a-cron-manifests-retries)
  for why a triggered cron job retries only at its next occurrence, and how to test it anyway.
- **Cleaning tables between tests while the scheduler runs.** Take the newest `metadata.id` before
  acting and read only rows after it
  ([Choose how the scheduler runs under test](https://traxsharp.net/docs/cross-cutting/e2e-testing#choose-how-the-scheduler-runs-under-test)).
- **Waiting with `Task.Delay`.** Poll for the row or the state with a timeout. `HygieneGuardFixture`
  refuses an unmarked fixed delay.
- **A host test against the development database.** Point the factory at the suite's own database
  with `UseSetting` on the connection string key the host reads.
- **Leaving a run stuck without saying so.** On Postgres, run `TraxInvariants` after each test,
  once the host has stopped, and mark a test that leaves a stuck run on purpose with
  `[LeavesStuckRuns("why")]` ([Check the database after each test](https://traxsharp.net/docs/reference/adopt-the-architecture-guards#check-the-database-after-each-test)).
- **Testing a JWT-authenticated host with hand-made tokens.** `Trax.Api.Auth.Jwt.Testing` signs
  test tokens and serves their keys ([TestJwksServer](https://traxsharp.net/docs/sdk-reference/api-auth/test-jwks-server)).

## Read next

- [Architecture guards](https://traxsharp.net/docs/reference/architecture-guards): every check and
  why it exists.
- [Make a junction cancellable](https://traxsharp.net/docs/cross-cutting/make-a-junction-cancellable#5-test-it):
  testing that a junction stops.
- [Testing](https://traxsharp.net/docs/sdk-reference/testing): every testing package.
