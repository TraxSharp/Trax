---
title: "5. Does this order look like fraud"
description: "Chapter 5 of the Trax book: stop an order that looks like fraud with a Gate a rule decider answers, record each decision, and try a stricter rule in shadow."
type: tutorial
area: general
parent: The Trax book
nav_order: 15
snippet_context: none
follows: book/the-shops-own-data
---

# 5. Does this order look like fraud

A stolen card is usually used fast and for a lot, often on an account that has never ordered
before. In this chapter `PlaceOrder` asks whether an order looks like fraud before it reserves any
stock or charges anyone, and refuses the ones that do. The answer comes from a rule today, and the
train is written so that a model can give it tomorrow without the train changing.

By the end, every answer is recorded with the run that asked, and a stricter rule is asked beside
the live one on every order, so we can see where the two would disagree before trusting the new
one.

This chapter starts from the end of [chapter 4](/docs/book/the-shops-own-data).

## A question, not an if

The obvious way to write this is an `if` in a junction. It works, and it hides three things: which
orders were stopped and why, which rule stopped them, and what a different rule would have done.
Trax can do better when the branch is declared on the chain as a question: the chain names every
track it can take, a **decider** answers the question on each run, and Trax records the answer.

There are three kinds of question. This one has two answers, so it is a yes or no question, routed
by a `Gate`.

| Question | Routed by | Answer |
|---|---|---|
| Which of these options? | `Switch` | One member of an enum, with a confidence |
| Where on this scale? | `Scale` | A position on an ordered enum |
| How likely is yes? | `Gate` | A probability from 0 to 1 |

The decider can be a rule table, a model or, in a test, a script. The train does not know which.

## The question and what it is asked about

The question is a type, so the chain can name it and Trax can key its answers by it. Its words go
on the type, once: a model reads them, and so does anyone reading the record later.

By the end of the chapter the new files are:

```text
Shopfront/
├── Extensions/
│   └── ShopfrontServiceCollectionExtensions.cs
├── Fraud/
│   ├── LooksFraudulent.cs
│   ├── OrderRisk.cs
│   ├── FraudRules.cs
│   ├── ICandidateFraudDecider.cs
│   └── StricterFraudRules.cs
└── Trains/PlaceOrder/Junctions/
    ├── AssessRiskJunction.cs
    └── RefuseOrderJunction.cs
```

Fraud is the shop's policy rather than one train's, so it gets a folder outside `Trains/`, like the
catalogue.

`Fraud/LooksFraudulent.cs`:

```csharp compile=app file=Fraud/LooksFraudulent.cs
using Trax.Core.Decisions;

namespace Shopfront.Fraud;

[Asks(
    "Does this order look like a stolen card being used?",
    Yes = "Hold it: it matches how stolen cards are used.",
    No = "Take it: nothing about it is unusual."
)]
public sealed class LooksFraudulent;
```

A decider is asked about one value in Memory. The cart and the input together hold too much, and
not the one thing that matters most: whether this customer has ordered before. A value of its own
says exactly what the question is about.

`Fraud/OrderRisk.cs`:

```csharp compile=app file=Fraud/OrderRisk.cs
namespace Shopfront.Fraud;

public record OrderRisk(string CustomerId, decimal Total, bool FirstOrder);
```

A junction gathers it.

`Trains/PlaceOrder/Junctions/AssessRiskJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/AssessRiskJunction.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Fraud;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class AssessRiskJunction(IShopfrontDbContext db)
    : EffectJunction<(PlaceOrderInput Order, PricedCart Cart), OrderRisk>
{
    public override async Task<OrderRisk> Run((PlaceOrderInput Order, PricedCart Cart) input)
    {
        var ordered = await db.Orders.AnyAsync(
            o => o.CustomerId == input.Order.CustomerId,
            CancellationToken
        );
        return new OrderRisk(input.Order.CustomerId, input.Cart.Total, FirstOrder: !ordered);
    }
}
```

## The tracks

A held order stops with a message for the customer. The junction that does it has nothing to
return, so it returns `Unit`, the type for "no value".

`Trains/PlaceOrder/Junctions/RefuseOrderJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/RefuseOrderJunction.cs
using Shopfront.Fraud;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class RefuseOrderJunction(ILogger<RefuseOrderJunction> logger)
    : EffectJunction<OrderRisk, Unit>
{
    public override Task<Unit> Run(OrderRisk risk)
    {
        logger.LogWarning("Held a first order of {Total} from {CustomerId}", risk.Total, risk.CustomerId);
        throw new TrainException(
            "We could not take this order without a check. You have not been charged."
        );
    }
}
```

The gate goes after pricing and the risk, and before anything is reserved or charged, so a held
order has nothing to undo.

`Trains/PlaceOrder/PlaceOrderTrain.cs`:

```csharp compile=app file=Trains/PlaceOrder/PlaceOrderTrain.cs
using Shopfront.Fraud;
using Shopfront.Trains.PlaceOrder.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.PlaceOrder;

public class PlaceOrderTrain : ServiceTrain<PlaceOrderInput, PlaceOrderOutput>, IPlaceOrderTrain
{
    protected override Task<Either<Exception, PlaceOrderOutput>> Junctions() =>
        Chain<PriceCartJunction>()
            .Chain<AssessRiskJunction>()
            .Gate<OrderRisk, LooksFraudulent>(gate =>
                gate.Yes(hold => hold.Chain<RefuseOrderJunction>())
                    .No(take => take)
                    .Shadow<ICandidateFraudDecider>()
                    .WaitForShadows(TimeSpan.FromSeconds(1))
            )
            .Chain<ReserveStockJunction>()
            .Chain<TakePaymentJunction>()
            .Chain<ConfirmOrderJunction>()
            .Resolve();
}
```

`Gate<OrderRisk, LooksFraudulent>` asks the decider how likely the answer is yes, about the
`OrderRisk` in Memory. At 0.5 or more the train takes the `Yes` track, which refuses the order;
below it, the `No` track, which adds nothing and carries on to `ReserveStockJunction`. Both tracks
are part of the chain, so the startup check that refused a misordered chain in chapter 1 checks
both: whichever is taken, `ReserveStockJunction` still finds its `PricedCart`.

The last two lines are the shadow, which comes later in the chapter.

## A rule decides

The rule the shop uses is policy, not judgement: hold any first order of 100 or more. Policy is
written as a `RuleDecider`, which maps the value asked about to an answer and is always certain of
it.

`Fraud/FraudRules.cs`:

```csharp compile=app file=Fraud/FraudRules.cs
using Trax.Core.Decisions;

namespace Shopfront.Fraud;

public static class FraudRules
{
    // A stolen card's first use is usually its largest: hold first orders of 100 or more.
    public static RuleDecider Live() =>
        new RuleDecider().YesNo<OrderRisk, LooksFraudulent>(risk =>
            risk.FirstOrder && risk.Total >= 100m
        );
}
```

## The rule we might move to

Someone thinks 100 is too high and wants to hold first orders from 60. Switching the live rule
would show what it catches only by stopping real customers. A **shadow** decider is asked every
question the live one is, beside it, and its answer is recorded with whether it agreed. It is
never acted on: a shadow that fails, disagrees or is slow changes nothing about the order.

A shadow is named by type, so the candidate gets a type of its own.

`Fraud/ICandidateFraudDecider.cs`:

```csharp compile=app file=Fraud/ICandidateFraudDecider.cs
using Trax.Core.Decisions;

namespace Shopfront.Fraud;

public interface ICandidateFraudDecider : IDecider;
```

`Fraud/StricterFraudRules.cs`:

```csharp compile=app file=Fraud/StricterFraudRules.cs
using Trax.Core.Decisions;

namespace Shopfront.Fraud;

// Holds first orders from 60. Asked beside the live rule, never acted on.
public sealed class StricterFraudRules : ICandidateFraudDecider
{
    private readonly RuleDecider rules = new RuleDecider().YesNo<OrderRisk, LooksFraudulent>(risk =>
        risk.FirstOrder && risk.Total >= 60m
    );

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken) =>
        rules.Decide(request, cancellationToken);
}
```

`.Shadow<ICandidateFraudDecider>()` on the gate puts every question to it as well.
`.WaitForShadows(TimeSpan.FromSeconds(1))` bounds how long a run waits for it once the live answer
is in. The default is five seconds, which every order would pay for a slow candidate.

## Register Shopfront in one place

The tests have copied `Program.cs`'s registrations since chapter 2, and the list keeps growing.
From here on both call one method, so a test runs with what the application runs with.

`Extensions/ShopfrontServiceCollectionExtensions.cs`:

```csharp compile=app file=Extensions/ShopfrontServiceCollectionExtensions.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Failures;
using Shopfront.Fraud;
using Shopfront.Inventory;
using Shopfront.Payments;
using Trax.Core.Decisions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Services.FailureClassifier;

namespace Shopfront.Extensions;

public static class ShopfrontServiceCollectionExtensions
{
    // Everything Shopfront's trains need, apart from Trax itself.
    public static IServiceCollection AddShopfront(
        this IServiceCollection services,
        string connectionString
    ) =>
        services
            .AddDomainDataContext<IShopfrontDbContext, ShopfrontDbContext>(options =>
                options.UseNpgsql(connectionString)
            )
            .AddScoped<Catalogue>()
            .AddSingleton<IPaymentProvider, FakePaymentProvider>()
            .AddSingleton<IFailureClassifier, ShopfrontFailureClassifier>()
            .AddSingleton<IDecider>(FraudRules.Live())
            .AddSingleton<ICandidateFraudDecider, StricterFraudRules>();
}
```

One `IDecider` serves every question in every train. The shadow is registered under the type the
gate names.

`Program.cs`:

```csharp compile=app file=Program.cs
using Shopfront.Data;
using Shopfront.Extensions;
using Shopfront.Trains.PlaceOrder;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Shopfront")
    ?? throw new InvalidOperationException("ConnectionStrings:Shopfront is not set.");

builder.Services.AddShopfront(connectionString);

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .SaveTrainParameters()
                .AddJunctionLogger(serializeJunctionData: true)
                .AddDecisionRecording()
        )
        .AddMediator(typeof(Program).Assembly)
);

var app = builder.Build();

await app.Services.EnsureSchemaCreatedAsync<ShopfrontDbContext>();

app.MapPost(
    "/orders",
    async (PlaceOrderInput input, IPlaceOrderTrain train, CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(await train.Run(input, cancellationToken));
        }
        catch (TrainException ex)
        {
            return Results.UnprocessableEntity(ex.Message);
        }
    }
);

await app.RunAsync();
```

`AddDecisionRecording` writes every question a run asks to `trax.decision`, with the answer, the
decider that gave it, each shadow's answer and whether it agreed, and the track taken. It writes as
each decision is made, before the train acts on it, so a run that fails afterwards still shows what
it decided. It needs the data provider before it.

Each answer is stored with a hash of the value it was about, not the value. Without a key the hash
is a plain SHA-256; a deployment that wants it keyed sets one, as
[Keying the state hash](/docs/effect/decisions#keying-the-state-hash) describes.

## Run it

```bash
dotnet run --urls http://localhost:5080
```

`grace` has never ordered. Her first order comes to 74.00, under the live rule's 100:

```bash
curl -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"grace","paymentToken":"tok_visa","lines":[{"sku":"TEE","quantity":2},{"sku":"MUG","quantity":2}]}'
```

It goes through. `hopper` has never ordered either, and orders nine mugs, 108.00:

```bash
curl -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"hopper","paymentToken":"tok_visa","lines":[{"sku":"MUG","quantity":9}]}'
```

```json
"We could not take this order without a check. You have not been charged."
```

Nothing was reserved and nobody was charged: the train stopped at the gate.

## Read the decisions

```bash
docker exec shopfront-postgres psql -U postgres -d shopfront -c "
  select metadata_id as run, question_key, answer->>'probability' as yes,
         routes->0->>'track' as track,
         shadows->0->'answer'->>'probability' as shadow_yes,
         shadows->0->>'agrees' as agrees
  from trax.decision order by id"
```

```text
 run |  question_key   | yes | track | shadow_yes | agrees
-----+-----------------+-----+-------+------------+--------
   1 | LooksFraudulent | 0   | No    | 1          | false
   2 | LooksFraudulent | 1   | Yes   | 1          | true
(2 rows)
```

Your run ids are higher, after the runs of the earlier chapters. Each row is one question one run
asked. `answer` is what the live rule said and `routes` the track the gate took on it. `shadows`
holds what the stricter rule said and whether it agreed: on `grace`'s order it would have held
74.00, and the live rule took it. That disagreement is the evidence for or against the stricter
rule, gathered without stopping a single customer. When the candidate has agreed often enough on
the orders that matter, it becomes the live `IDecider` and the `Shadow` line comes off the gate.

## Test it

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
