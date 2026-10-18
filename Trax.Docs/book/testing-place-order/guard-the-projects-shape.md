---
title: "2.6 Guard the project's shape"
description: "Chapter 2 of the Trax book, section 6: Guard the project's shape."
type: tutorial
area: general
parent: "2. Testing PlaceOrder"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/testing-place-order/test-the-host
---

# 2.6 Guard the project's shape

Some rules are about the whole project rather than one test: every train has an interface to be
called and recorded by, and tests do not sleep, skip or assert the old way. Trax ships those
checks as test fixtures. A project subclasses them and writes no test bodies.

`tests/Shopfront.Tests/ArchitectureGuards.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/ArchitectureGuards.cs
using System.Reflection;
using Shopfront.Trains.PlaceOrder;
using Trax.Core.Testing.Fixtures;
using Trax.Mediator.Testing;

namespace Shopfront.Tests;

[TestFixture]
public sealed class ShopfrontTrainGuards : TrainGuardFixture
{
    protected override IReadOnlyList<Assembly> TrainAssemblies =>
        [typeof(IPlaceOrderTrain).Assembly];
}

[TestFixture]
public sealed class ShopfrontHygieneGuards : HygieneGuardFixture;
```

`TrainGuardFixture` fails when a train has no companion interface. `HygieneGuardFixture` reads the
source under `tests/`, relative to the folder holding the solution, and fails on an `[Ignore]`, a
fixed `Task.Delay` or `Thread.Sleep`, and NUnit's `Assert.That`. That last one is why these tests
assert with AwesomeAssertions: its failures say what was expected and why, in the words of the
`because` argument.

## SDK Reference

> [ITrainBus](/docs/sdk-reference/mediator-api/i-train-bus) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [Metadata](/docs/effect/metadata) | [Mediator testing](/docs/sdk-reference/testing/mediator-testing) | [Architecture guards](/docs/reference/architecture-guards)

---

Next: [2.7 Run them](/docs/book/testing-place-order/run-them)
