---
title: "2.4 Test the train"
description: "Chapter 2 of the Trax book, section 4: Test the train."
type: tutorial
area: general
parent: "2. Testing PlaceOrder"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/testing-place-order/test-a-junction
---

# 2.4 Test the train

A unit test cannot tell that the junctions fit together, or that the run was recorded. A train
test builds a container with the same Trax registration as `Program.cs`, runs `PlaceOrder` through
the train bus as any caller would, and reads back the record.

`tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs
using Microsoft.Extensions.DependencyInjection;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Trax.Core.Exceptions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class PlaceOrderTrainTests
{
    private ServiceProvider _provider = null!;

    [SetUp]
    public void BuildTheContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Catalogue>();
        services.AddSingleton<IPaymentProvider, FakePaymentProvider>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
                .AddMediator(typeof(IPlaceOrderTrain).Assembly)
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public ValueTask DisposeTheContainer() => _provider.DisposeAsync();

    [Test]
    public async Task RunAsync_InStock_ConfirmsTheOrderAndRecordsTheRun()
    {
        // A customer no other test uses, to find this run's record among any others.
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var output = await bus.RunAsync<PlaceOrderOutput>(
            new PlaceOrderInput { CustomerId = customerId, Lines = [new("MUG", 2)] }
        );

        output.Total.Should().Be(24.00m);
        var run = RunFor(customerId);
        run.TrainState.Should().Be(TrainState.Completed);
        run.Output.Should().Contain(output.TransactionId);
    }

    [Test]
    public async Task RunAsync_OutOfStock_FailsAtReserveStockAndRecordsWhy()
    {
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () =>
            bus.RunAsync<PlaceOrderOutput>(
                new PlaceOrderInput { CustomerId = customerId, Lines = [new("CAP", 1)] }
            );

        await act.Should().ThrowAsync<TrainException>().WithMessage("CAP is out of stock.");
        var run = RunFor(customerId);
        run.TrainState.Should().Be(TrainState.Failed);
        run.FailureJunction.Should().Be("ReserveStockJunction");
        run.FailureReason.Should().Be("CAP is out of stock.");
    }

    private Metadata RunFor(string customerId)
    {
        using var scope = _provider.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return data
            .Metadatas.Where(m => m.Name == typeof(IPlaceOrderTrain).FullName)
            .AsEnumerable()
            .Single(m => m.Input?.Contains(customerId) == true);
    }
}
```

Each run is recorded under the full name of `IPlaceOrderTrain`, with its input and output as JSON,
because chapter 1 turned on `SaveTrainParameters`. A failed run also records the junction that
threw and its message. Chapter 3 reads the same records from Postgres.

The container is built and disposed around every test, so each test starts with an empty record
and a full catalogue.

## SDK Reference

> [ITrainBus](/docs/sdk-reference/mediator-api/i-train-bus) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [Metadata](/docs/effect/metadata) | [Mediator testing](/docs/sdk-reference/testing/mediator-testing) | [Architecture guards](/docs/reference/architecture-guards)

---

Next: [2.5 Test the host](/docs/book/testing-place-order/test-the-host)
