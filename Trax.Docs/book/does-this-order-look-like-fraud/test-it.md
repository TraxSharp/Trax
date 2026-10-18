---
title: "5.7 Test it"
description: "Chapter 5 of the Trax book, section 7: Test it."
type: tutorial
area: general
parent: "5. Does this order look like fraud"
grand_parent: The Trax book
nav_order: 7
snippet_context: none
follows: book/does-this-order-look-like-fraud/read-the-decisions
---

# 5.7 Test it

A decision is tested like any other dependency: replace the decider. `ScriptedDecider` answers each
question with what the test says and keeps every request it was asked. Registered after
`AddShopfront`, it replaces the rule.

`tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shopfront.Extensions;
using Shopfront.Fraud;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Decisions;
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

    private async Task Build(Action<IServiceCollection>? replace = null)
    {
        await TestDatabase.Reset();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddShopfront(TestDatabase.ConnectionString);
        replace?.Invoke(services);
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
                .AddMediator(typeof(IPlaceOrderTrain).Assembly)
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public ValueTask DisposeTheContainer() => _provider.DisposeAsync();

    private static PlaceOrderInput Order(
        string customerId,
        string paymentToken,
        string sku,
        int quantity = 1
    ) =>
        new()
        {
            CustomerId = customerId,
            Lines = [new OrderLine(sku, quantity)],
            PaymentToken = paymentToken,
        };

    [Test]
    public async Task RunAsync_InStock_StoresTheOrderAndMasksTheReceipt()
    {
        await Build();
        // A customer no other test uses, to find this run's record among any others.
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var output = await bus.RunAsync<PlaceOrderOutput>(Order(customerId, "tok_visa", "MUG"));

        var run = RunFor(customerId);
        run.TrainState.Should().Be(TrainState.Completed);
        run.Output.Should().Contain(output.TransactionId).And.NotContain(output.ReceiptUrl);
        await using var db = TestDatabase.NewContext();
        (await db.Orders.SingleAsync()).Id.Should().Be(output.OrderId);
        (await TestDatabase.StockOf("MUG")).Should().Be(9);
    }

    [Test]
    public async Task RunAsync_LargeFirstOrder_IsHeldBeforeStockOrPayment()
    {
        await Build();
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(customerId, "tok_visa", "MUG", 9));

        await act.Should().ThrowAsync<TrainException>().WithMessage("*You have not been charged.");
        RunFor(customerId).FailureJunction.Should().Be("RefuseOrderJunction");
        (await TestDatabase.StockOf("MUG")).Should().Be(10);
    }

    [Test]
    public async Task RunAsync_DeciderSaysYes_HoldsEvenASmallOrder()
    {
        var decider = new ScriptedDecider().YesNo<LooksFraudulent>(0.9);
        await Build(services => services.AddSingleton<IDecider>(decider));
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(customerId, "tok_visa", "MUG"));

        await act.Should().ThrowAsync<TrainException>();
        decider.Requests.Should().ContainSingle()
            .Which.State.Should().Be(new OrderRisk(customerId, 12.00m, FirstOrder: true));
    }

    [Test]
    public async Task RunAsync_OutOfStock_FailsAtReserveStockAsPermanent()
    {
        await Build();
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(customerId, "tok_visa", "CAP"));

        await act.Should().ThrowAsync<TrainException>().WithMessage("CAP is out of stock.");
        var run = RunFor(customerId);
        run.TrainState.Should().Be(TrainState.Failed);
        run.FailureJunction.Should().Be("ReserveStockJunction");
        run.FailureClass.Should().Be(FailureClass.Permanent);
    }

    [Test]
    public async Task RunAsync_Declined_FailsAtTakePaymentAndStoresNoOrder()
    {
        await Build();
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var act = () =>
            bus.RunAsync<PlaceOrderOutput>(
                Order(customerId, FakePaymentProvider.DeclinedToken, "MUG")
            );

        await act.Should().ThrowAsync<TrainException>().WithMessage("The card was declined.");
        var run = RunFor(customerId);
        run.FailureJunction.Should().Be("TakePaymentJunction");
        run.FailureClass.Should().Be(FailureClass.Permanent);
        await using var db = TestDatabase.NewContext();
        (await db.Orders.AnyAsync()).Should().BeFalse();
        (await TestDatabase.StockOf("MUG")).Should().Be(10);
    }

    [Test]
    public async Task RunAsync_FifthCharge_TimesOutAsTransient()
    {
        await Build();
        var bus = _provider.GetRequiredService<ITrainBus>();
        for (var i = 0; i < 4; i++)
            await bus.RunAsync<PlaceOrderOutput>(Order(Guid.NewGuid().ToString(), "tok_visa", "MUG"));
        var customerId = Guid.NewGuid().ToString();

        var act = () => bus.RunAsync<PlaceOrderOutput>(Order(customerId, "tok_visa", "MUG"));

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        var run = RunFor(customerId);
        run.FailureJunction.Should().Be("TakePaymentJunction");
        run.FailureClass.Should().Be(FailureClass.Transient);
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

The third test is the one a scripted decider is for. It answers yes to an order the rule would
take, which proves the train follows its decider rather than any rule of its own, and its
`Requests` show exactly what a model in the rule's place would have been asked about.

The rules are tested as rules, through the junction that gathers what they read and the decider
itself.

`tests/Shopfront.Tests/UnitTests/AssessRiskJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/AssessRiskJunctionTests.cs
using Shopfront.Data.Models;
using Shopfront.Fraud;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class AssessRiskJunctionTests
{
    [SetUp]
    public Task ResetTheDatabase() => TestDatabase.Reset();

    private static PlaceOrderInput Order(string customerId) =>
        new()
        {
            CustomerId = customerId,
            Lines = [new OrderLine("MUG", 1)],
            PaymentToken = "tok_visa",
        };

    private static readonly PricedCart Cart = new([new PricedLine("MUG", 1, 12.00m)], 12.00m);

    [Test]
    public async Task Run_NoEarlierOrder_IsAFirstOrder()
    {
        await using var db = TestDatabase.NewContext();

        var risk = await new AssessRiskJunction(db).Run((Order("ada"), Cart));

        risk.Should().Be(new OrderRisk("ada", 12.00m, FirstOrder: true));
    }

    [Test]
    public async Task Run_AnEarlierOrder_IsNotAFirstOrder()
    {
        await using (var seed = TestDatabase.NewContext())
        {
            seed.Orders.Add(
                new Order
                {
                    CustomerId = "ada",
                    TransactionId = "tx_earlier",
                    Total = 12.00m,
                }
            );
            await seed.SaveChangesAsync();
        }
        await using var db = TestDatabase.NewContext();

        var risk = await new AssessRiskJunction(db).Run((Order("ada"), Cart));

        risk.FirstOrder.Should().BeFalse();
    }
}
```

`tests/Shopfront.Tests/UnitTests/FraudRulesTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/FraudRulesTests.cs
using Shopfront.Fraud;
using Trax.Core.Decisions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class FraudRulesTests
{
    // The question as a Gate<OrderRisk, LooksFraudulent> asks it.
    private static DecisionRequest Asking(OrderRisk risk) =>
        new(
            "PlaceOrder",
            risk,
            [new YesNoQuestion(QuestionKey.For<LooksFraudulent>(), "Does this order look like fraud?", null, null)]
        );

    private static async Task<double> ProbabilityOfYes(IDecider decider, OrderRisk risk)
    {
        var result = await decider.Decide(Asking(risk), CancellationToken.None);
        return ((YesNoAnswer)result.Answers[QuestionKey.For<LooksFraudulent>()]).Probability;
    }

    [TestCase(99.99, true, 0.0)]
    [TestCase(100.00, true, 1.0)]
    [TestCase(500.00, false, 0.0)]
    public async Task Live_HoldsFirstOrdersFrom100(decimal total, bool firstOrder, double yes) =>
        (await ProbabilityOfYes(FraudRules.Live(), new OrderRisk("ada", total, firstOrder)))
            .Should()
            .Be(yes);

    [TestCase(59.99, true, 0.0)]
    [TestCase(60.00, true, 1.0)]
    public async Task Stricter_HoldsFirstOrdersFrom60(decimal total, bool firstOrder, double yes) =>
        (await ProbabilityOfYes(new StricterFraudRules(), new OrderRisk("ada", total, firstOrder)))
            .Should()
            .Be(yes);
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    43, Skipped:     0, Total:    43, Duration: 14 s - Shopfront.Tests.dll (net10.0)
```

## What we have

`PlaceOrder` asks a question before it takes anything, and the answer comes from a decider it does
not know the shape of: a rule today, a model or a script on another day. Every answer is recorded
with the run that asked, and a candidate rule is weighed against the live one on every order
without touching any.

That ends the first part. `PlaceOrder` is a train of small junctions, tested at every level,
recorded in full, and careful with stock, payments and secrets. Anyone can call it, though, and
for anyone. The next part puts Shopfront in front of the people it serves: customers who sign in
and order as themselves, and staff who see every order.

## SDK Reference

> [Gate](/docs/sdk-reference/train-methods/gate) | [Decide](/docs/sdk-reference/train-methods/decide) | [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [Write a decider](/docs/core/write-a-decider) | [Try a decider in shadow](/docs/core/try-a-decider-in-shadow) | [Test a train's decisions](/docs/core/test-train-decisions)

---

Next: [6. Customers and staff](/docs/book/customers-and-staff)
