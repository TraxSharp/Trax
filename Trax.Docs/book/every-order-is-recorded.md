---
title: "3. Every order is recorded"
description: "Chapter 3 of the Trax book: record every run of PlaceOrder in Postgres, make payments fail, give the stock back, mask a secret and read the failed runs back."
type: tutorial
area: general
parent: The Trax book
nav_order: 13
snippet_context: none
follows: book/testing-place-order
---

# 3. Every order is recorded

Trax has recorded every run of `PlaceOrder` since chapter 1, but in memory, so the record went
with the process. In this chapter we move it to Postgres and then give it something worth
reading: payments that fail. By the end, a declined card or a provider that does not answer stops
the order, gives its stock back, and leaves a row that says which junction failed, why, and whether
trying again could help. The receipt link the provider hands back is kept out of every copy Trax
writes.

This chapter starts from the end of [chapter 2](/docs/book/testing-place-order).

## Start Postgres

Run Postgres in a container, with a database for Shopfront:

```bash
docker run -d --name shopfront-postgres -p 5432:5432 \
  -e POSTGRES_PASSWORD=shopfront -e POSTGRES_DB=shopfront postgres:17
```

Shopfront reads its connection string from configuration. Replace `appsettings.json`:

```json file=appsettings.json
{
  "ConnectionStrings": {
    "Shopfront": "Host=localhost;Database=shopfront;Username=postgres;Password=shopfront"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider.JunctionLoggerProvider": "Debug"
    }
  },
  "AllowedHosts": "*"
}
```

## Packages

The application swaps in-memory storage for Postgres and adds the junction logger. The tests keep
in-memory storage for the train tests, so they take that package over. Add the new versions to
`Directory.Packages.props`:

```xml file=Directory.Packages.props
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Trax.Core" Version="1.61.0" />
    <PackageVersion Include="Trax.Core.Testing" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.InMemory" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.Postgres" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.JunctionProvider.Logging" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Provider.Parameter" Version="1.61.0" />
    <PackageVersion Include="Trax.Mediator" Version="1.61.0" />
    <PackageVersion Include="Trax.Mediator.Testing" Version="1.61.0" />
  </ItemGroup>

  <ItemGroup>
    <PackageVersion Include="AwesomeAssertions" Version="9.6.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageVersion Include="NUnit" Version="5.0.0" />
    <PackageVersion Include="NUnit.Analyzers" Version="4.15.0" />
    <PackageVersion Include="NUnit3TestAdapter" Version="6.3.0" />
  </ItemGroup>
</Project>
```

`Shopfront.csproj`:

```xml file=Shopfront.csproj
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- The tests live under tests/, in a project of their own. -->
    <DefaultItemExcludes>$(DefaultItemExcludes);tests/**</DefaultItemExcludes>
  </PropertyGroup>

  <ItemGroup>
    <!-- The junctions are internal; their tests are the one project that may see them. -->
    <InternalsVisibleTo Include="Shopfront.Tests" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Trax.Core" />
    <PackageReference Include="Trax.Effect" />
    <PackageReference Include="Trax.Effect.Data.Postgres" />
    <PackageReference Include="Trax.Effect.JunctionProvider.Logging" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" />
    <PackageReference Include="Trax.Mediator" />
  </ItemGroup>
</Project>
```

`tests/Shopfront.Tests/Shopfront.Tests.csproj`:

```xml file=tests/Shopfront.Tests/Shopfront.Tests.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Shopfront.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="AwesomeAssertions" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NUnit" />
    <PackageReference Include="NUnit.Analyzers" />
    <PackageReference Include="NUnit3TestAdapter" />
    <PackageReference Include="Trax.Core.Testing" />
    <PackageReference Include="Trax.Effect.Data.InMemory" />
    <PackageReference Include="Trax.Mediator.Testing" />
  </ItemGroup>
</Project>
```

## Record runs in Postgres

`Program.cs`:

```csharp compile=app file=Program.cs
using Shopfront.Failures;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.FailureClassifier;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Shopfront")
    ?? throw new InvalidOperationException("ConnectionStrings:Shopfront is not set.");

builder.Services.AddSingleton<Catalogue>();
builder.Services.AddSingleton<IPaymentProvider, FakePaymentProvider>();
builder.Services.AddSingleton<IFailureClassifier, ShopfrontFailureClassifier>();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .SaveTrainParameters()
                .AddJunctionLogger(serializeJunctionData: true)
        )
        .AddMediator(typeof(Program).Assembly)
);

var app = builder.Build();

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

app.Run();
```

`UsePostgres` replaces `UseInMemory`. When the application starts, it creates the `trax` schema in
the database and brings it up to date, before the host is built, so the database has to be
reachable by then. Everything after it in `AddEffects` writes through it:

- `SaveTrainParameters` stores each run's input and output with its row, as it did in memory.
- `AddJunctionLogger` logs an entry for each junction as it finishes: its name, its timing, whether
  it succeeded, and, with `serializeJunctionData: true`, what it returned.

The junction logger writes at `Debug`, which the default configuration hides. The last line under
`Logging` in `appsettings.json` shows it, by its category name.

The data provider comes first in `AddEffects`, because the effects after it write through it.
[Registration order](/docs/reference/registration-order) lists what fails when that order is
wrong.

Run it and place an order as in chapter 1. The run is a row now. Read it with `psql` inside the
container:

```bash
docker exec shopfront-postgres psql -U postgres -d shopfront \
  -c "select id, name, train_state, start_time from trax.metadata order by id"
```

Stop the application, start it again, and the row is still there.

## The provider fails

A real payment provider declines cards and sometimes does not answer. Ours will do both. The
customer pays with a payment token, the kind a provider's card form hands the browser: a reference
to the card that only the provider can use, so Shopfront never sees the card itself.

`Trains/PlaceOrder/PlaceOrderInput.cs`:

```csharp compile=app file=Trains/PlaceOrder/PlaceOrderInput.cs
using Shopfront.Trains.PlaceOrder.Models;

namespace Shopfront.Trains.PlaceOrder;

public record PlaceOrderInput
{
    public required string CustomerId { get; init; }
    public required List<OrderLine> Lines { get; init; }
    public required string PaymentToken { get; init; }
}
```

The provider answers a charge with a transaction id and a receipt link. Anyone holding the link
can open the receipt.

`Payments/ChargeResult.cs`:

```csharp compile=app file=Payments/ChargeResult.cs
namespace Shopfront.Payments;

public record ChargeResult(string TransactionId, string ReceiptUrl);
```

`Payments/PaymentDeclinedException.cs`:

```csharp compile=app file=Payments/PaymentDeclinedException.cs
namespace Shopfront.Payments;

public class PaymentDeclinedException(string message) : Exception(message);
```

`Payments/IPaymentProvider.cs`:

```csharp compile=app file=Payments/IPaymentProvider.cs
namespace Shopfront.Payments;

public interface IPaymentProvider
{
    Task<ChargeResult> Charge(
        string customerId,
        string paymentToken,
        decimal amount,
        CancellationToken cancellationToken
    );
}
```

The fake declines one test token, as real providers' test modes do, and times out on every fifth
charge.

`Payments/FakePaymentProvider.cs`:

```csharp compile=app file=Payments/FakePaymentProvider.cs
namespace Shopfront.Payments;

public class FakePaymentProvider : IPaymentProvider
{
    public const string DeclinedToken = "tok_chargeDeclined";

    private int charges;

    public Task<ChargeResult> Charge(
        string customerId,
        string paymentToken,
        decimal amount,
        CancellationToken cancellationToken
    )
    {
        if (paymentToken == DeclinedToken)
            throw new PaymentDeclinedException("The card was declined.");

        if (Interlocked.Increment(ref charges) % 5 == 0)
            throw new TimeoutException("The payment provider did not answer.");

        var transactionId = $"tx_{Guid.NewGuid():N}"[..11];
        return Task.FromResult(
            new ChargeResult(
                transactionId,
                $"https://pay.example/receipts/{transactionId}?key={Guid.NewGuid():N}"
            )
        );
    }
}
```

## Give the stock back

Stock is reserved before the payment is taken. When the payment fails, the order stops, and the
stock it reserved has to go back on the shelf. The catalogue learns to release a reservation.

`Inventory/Catalogue.cs`:

```csharp compile=app file=Inventory/Catalogue.cs
namespace Shopfront.Inventory;

// The catalogue and its stock, in memory for now.
public class Catalogue
{
    private readonly Dictionary<string, (decimal Price, int Stock)> items = new()
    {
        ["MUG"] = (12.00m, 10),
        ["TEE"] = (25.00m, 3),
        ["CAP"] = (18.50m, 0),
    };

    public decimal? PriceOf(string sku) => items.TryGetValue(sku, out var item) ? item.Price : null;

    // Reserves every line or none of them. Returns the first SKU that is short, or null.
    public string? TryReserve(IReadOnlyList<(string Sku, int Quantity)> lines)
    {
        lock (items)
        {
            var shortSku = lines
                .Where(l => items[l.Sku].Stock < l.Quantity)
                .Select(l => l.Sku)
                .FirstOrDefault();
            if (shortSku is not null)
                return shortSku;

            foreach (var (sku, quantity) in lines)
                items[sku] = items[sku] with { Stock = items[sku].Stock - quantity };
            return null;
        }
    }

    // Puts reserved stock back.
    public void Release(IReadOnlyList<(string Sku, int Quantity)> lines)
    {
        lock (items)
        {
            foreach (var (sku, quantity) in lines)
                items[sku] = items[sku] with { Stock = items[sku].Stock + quantity };
        }
    }
}
```

The payment junction is the one that knows the payment failed, so it gives the stock back. It
asks Memory for the reservation as well, which is the `StockReservation` that has sat there unread
since chapter 1.

`Trains/PlaceOrder/Junctions/TakePaymentJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/TakePaymentJunction.cs
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class TakePaymentJunction(
    IPaymentProvider payments,
    Catalogue catalogue,
    ILogger<TakePaymentJunction> logger
) : EffectJunction<(PlaceOrderInput Order, PricedCart Cart, StockReservation Reservation), Payment>
{
    public override async Task<Payment> Run(
        (PlaceOrderInput Order, PricedCart Cart, StockReservation Reservation) input
    )
    {
        ChargeResult charge;
        try
        {
            charge = await payments.Charge(
                input.Order.CustomerId,
                input.Order.PaymentToken,
                input.Cart.Total,
                CancellationToken
            );
        }
        catch (Exception ex)
        {
            // However the charge failed, the order stops here, so its stock goes back.
            catalogue.Release([.. input.Cart.Lines.Select(l => (l.Sku, l.Quantity))]);
            logger.LogInformation("Released reservation {ReservationId}", input.Reservation.Id);

            if (ex is PaymentDeclinedException)
                throw new TrainException(ex.Message);
            throw;
        }

        logger.LogInformation(
            "Charged {Total} as {TransactionId}",
            input.Cart.Total,
            charge.TransactionId
        );
        return new Payment(charge.TransactionId, input.Cart.Total, charge.ReceiptUrl);
    }
}
```

A declined card is the customer's to fix, so its message goes back to them as a `TrainException`.
A timeout is not, and its message names our provider, so it is rethrown as it is: the endpoint
turns it into a `500`, and the run records what really happened.

## Keep the receipt link out of the record

The receipt link travels on through the train to the customer. Trax would write it into every copy
it keeps: the run's stored output, and the junction logger's entry for `TakePaymentJunction`.
`[TraxSensitive]` on a member masks it in each of those copies, while the train itself runs with
the real value.

`Trains/PlaceOrder/Models/Payment.cs`:

```csharp compile=app file=Trains/PlaceOrder/Models/Payment.cs
using Trax.Effect.Attributes;

namespace Shopfront.Trains.PlaceOrder.Models;

internal record Payment(string TransactionId, decimal Amount, [TraxSensitive] string ReceiptUrl);
```

`Trains/PlaceOrder/PlaceOrderOutput.cs`:

```csharp compile=app file=Trains/PlaceOrder/PlaceOrderOutput.cs
using Trax.Effect.Attributes;

namespace Shopfront.Trains.PlaceOrder;

public record PlaceOrderOutput(
    Guid OrderId,
    decimal Total,
    string TransactionId,
    [TraxSensitive] string ReceiptUrl
);
```

`Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs`:

```csharp compile=app file=Trains/PlaceOrder/Junctions/ConfirmOrderJunction.cs
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class ConfirmOrderJunction(ILogger<ConfirmOrderJunction> logger)
    : EffectJunction<(PricedCart Cart, Payment Payment), PlaceOrderOutput>
{
    public override Task<PlaceOrderOutput> Run((PricedCart Cart, Payment Payment) input)
    {
        var output = new PlaceOrderOutput(
            Guid.NewGuid(),
            input.Cart.Total,
            input.Payment.TransactionId,
            input.Payment.ReceiptUrl
        );
        logger.LogInformation("Confirmed order {OrderId}", output.OrderId);
        return Task.FromResult(output);
    }
}
```

The payment token on the input is left unmarked. It is a reference only the provider can use, and
an input that carries a reference rather than the secret itself is what keeps the secret out of
every copy, including the ones `[TraxSensitive]` does not reach.

## Say whether trying again could help

A failed run records the junction and the exception. What it cannot tell from those alone is
whether running the same order again could work. A declined card will be declined again; a
provider that timed out may well answer next time. Trax records that as a failure class, and asks
one `IFailureClassifier` the application registers.

`Failures/ShopfrontFailureClassifier.cs`:

```csharp compile=app file=Failures/ShopfrontFailureClassifier.cs
using Trax.Core.Exceptions;
using Trax.Effect.Services.FailureClassifier;

namespace Shopfront.Failures;

public class ShopfrontFailureClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception switch
        {
            // A message for the customer: the same order fails the same way however often it runs.
            TrainException => FailureClass.Permanent,
            // The provider did not answer this time; it may next time.
            TimeoutException => FailureClass.Transient,
            _ => null,
        };
}
```

`Program.cs` above already registers it. The class is only recorded: nothing in Trax retries or
gives up because of it. It turns "which failures were worth retrying" into a query, and it is what
an operator reads first on a failed run.

## Watch it fail

Start the application again:

```bash
dotnet run --urls http://localhost:5080
```

Place an order:

```bash
curl -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"ada","paymentToken":"tok_visa","lines":[{"sku":"MUG","quantity":1}]}'
```

```json
{"orderId":"03cfec30-1b48-4a08-805b-12fb5f530311","total":12.00,"transactionId":"tx_4ba708d1","receiptUrl":"https://pay.example/receipts/tx_4ba708d1?key=c265bec1af1b47f5abf2f4c90daeb9d6"}
```

The customer gets the receipt link. Now pay with the declined token:

```bash
curl -X POST http://localhost:5080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"ada","paymentToken":"tok_chargeDeclined","lines":[{"sku":"TEE","quantity":3}]}'
```

```json
"The card was declined."
```

That is a `422`, and the log shows the three T-shirts going back:

```text
info: Shopfront.Trains.PlaceOrder.Junctions.TakePaymentJunction[0]
      Released reservation 568df86f-59b2-437a-ab72-797459564555
fail: Trax.Effect.Services.ServiceTrain.ServiceTrain[0]
      Caught Exception (Trax.Core.Exceptions.TrainException) with Message (The card was declined.).
```

Place four more orders of one mug with `tok_visa`. The provider answers three of them, and the
fourth, its fifth charge since the application started, times out with a `500`:

```text
info: Shopfront.Trains.PlaceOrder.Junctions.TakePaymentJunction[0]
      Released reservation 26db9cae-1c44-47f8-a9aa-0f0d701ab713
fail: Trax.Effect.Services.ServiceTrain.ServiceTrain[0]
      Caught Exception (System.TimeoutException) with Message (The payment provider did not answer.).
```

## Read the runs back

```bash
docker exec shopfront-postgres psql -U postgres -d shopfront -c \
  "select id, train_state, failure_junction, failure_class, failure_reason
   from trax.metadata order by id"
```

```text
 id | train_state |  failure_junction   | failure_class |            failure_reason
----+-------------+---------------------+---------------+--------------------------------------
  1 | completed   |                     | unclassified  |
  2 | completed   |                     | unclassified  |
  3 | failed      | TakePaymentJunction | permanent     | The card was declined.
  4 | completed   |                     | unclassified  |
  5 | completed   |                     | unclassified  |
  6 | completed   |                     | unclassified  |
  7 | failed      | TakePaymentJunction | transient     | The payment provider did not answer.
(7 rows)
```

Row 1 is the order from before the provider could fail. Each failed run says where it stopped and
whether it is worth trying again. Look at a completed run's output:

```bash
docker exec shopfront-postgres psql -U postgres -d shopfront -c \
  "select output from trax.metadata where id = 6"
```

```text
                                                                 output
----------------------------------------------------------------------------------------------------------------------------------------
 {"total": 12.00, "orderId": "154e628d-b68b-43d7-935a-12a4f05835ca", "receiptUrl": {"_redacted": true}, "transactionId": "tx_89fc3d4d"}
(1 row)
```

The receipt link is masked. So is the junction logger's entry for `TakePaymentJunction`, here with
its input and output type names left out:

```text
dbug: Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider.JunctionLoggerProvider[0]
      {
        "id": 0,
        "trainName": "Shopfront.Trains.PlaceOrder.IPlaceOrderTrain",
        "name": "TakePaymentJunction",
        "externalId": "d2189d4c58aa4183a8d70566f3f8fdce",
        "trainExternalId": "d3f9d761dbe84887b9d7b859711f5a77",
        "trainMetadataId": 2,
        "startTimeUtc": "2026-10-18T19:07:03.1630118Z",
        "endTimeUtc": "2026-10-18T19:07:03.1644088Z",
        "state": "IsRight",
        "hasRan": true,
        "outputJson": "{\u0022transactionId\u0022:\u0022tx_c6c0dd1a\u0022,\u0022amount\u0022:12.00,\u0022receiptUrl\u0022:{\u0022_redacted\u0022:true}}"
      }
```

`trainMetadataId` is the run's row in `trax.metadata`, which ties each junction's entry to its
run.

## Test it

The tests follow the code. The train's input has a payment token now, so the tests that build one
give it one.

`tests/Shopfront.Tests/UnitTests/PriceCartJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/PriceCartJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Inventory;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class PriceCartJunctionTests
{
    private static PriceCartJunction Junction() =>
        new(new Catalogue(), NullLogger<PriceCartJunction>.Instance);

    private static PlaceOrderInput Order(params OrderLine[] lines) =>
        new()
        {
            CustomerId = "ada",
            Lines = [.. lines],
            PaymentToken = "tok_visa",
        };

    [Test]
    public async Task Run_TwoLines_PricesEachAndTotalsThem()
    {
        var cart = await Junction().Run(Order(new OrderLine("MUG", 2), new OrderLine("TEE", 1)));

        cart.Lines.Select(l => l.UnitPrice).Should().Equal(12.00m, 25.00m);
        cart.Total.Should().Be(49.00m);
    }

    [Test]
    public async Task Run_EmptyCart_Throws()
    {
        var act = () => Junction().Run(Order());

        await act.Should().ThrowAsync<TrainException>().WithMessage("The cart is empty.");
    }

    [Test]
    public async Task Run_UnknownSku_Throws()
    {
        var act = () => Junction().Run(Order(new OrderLine("HAT", 1)));

        await act.Should().ThrowAsync<TrainException>().WithMessage("HAT is not in the catalogue.");
    }

    [Test]
    public async Task Run_ZeroQuantity_Throws()
    {
        var act = () => Junction().Run(Order(new OrderLine("MUG", 0)));

        await act.Should()
            .ThrowAsync<TrainException>()
            .WithMessage("MUG: the quantity must be at least 1.");
    }
}
```

The payment junction's tests check the new promise: a failed charge puts the stock back. The fake
here can be told to fail.

`tests/Shopfront.Tests/UnitTests/TakePaymentJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/TakePaymentJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Junctions;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class TakePaymentJunctionTests
{
    private sealed class ScriptedPaymentProvider(Exception? failWith = null) : IPaymentProvider
    {
        public List<(string CustomerId, string PaymentToken, decimal Amount)> Charges { get; } = [];

        public Task<ChargeResult> Charge(
            string customerId,
            string paymentToken,
            decimal amount,
            CancellationToken cancellationToken
        )
        {
            Charges.Add((customerId, paymentToken, amount));
            return failWith is null
                ? Task.FromResult(new ChargeResult("tx_test", "https://pay.example/receipts/tx_test"))
                : Task.FromException<ChargeResult>(failWith);
        }
    }

    private static readonly PlaceOrderInput Order = new()
    {
        CustomerId = "ada",
        Lines = [new OrderLine("TEE", 3)],
        PaymentToken = "tok_visa",
    };

    private static readonly PricedCart Cart = new([new PricedLine("TEE", 3, 25.00m)], 75.00m);

    // A catalogue whose three T-shirts are reserved, as ReserveStockJunction leaves it.
    private static Catalogue ReservedCatalogue()
    {
        var catalogue = new Catalogue();
        catalogue.TryReserve([("TEE", 3)]).Should().BeNull();
        return catalogue;
    }

    private static TakePaymentJunction Junction(IPaymentProvider payments, Catalogue catalogue) =>
        new(payments, catalogue, NullLogger<TakePaymentJunction>.Instance);

    [Test]
    public async Task Run_Charged_ReturnsThePaymentWithItsReceipt()
    {
        var payments = new ScriptedPaymentProvider();

        var payment = await Junction(payments, ReservedCatalogue())
            .Run((Order, Cart, new StockReservation(Guid.NewGuid())));

        payments.Charges.Should().Equal(("ada", "tok_visa", 75.00m));
        payment
            .Should()
            .Be(new Payment("tx_test", 75.00m, "https://pay.example/receipts/tx_test"));
    }

    [Test]
    public async Task Run_Declined_ThrowsTheReasonAndReleasesTheStock()
    {
        var catalogue = ReservedCatalogue();
        var payments = new ScriptedPaymentProvider(
            new PaymentDeclinedException("The card was declined.")
        );

        var act = () =>
            Junction(payments, catalogue).Run((Order, Cart, new StockReservation(Guid.NewGuid())));

        await act.Should().ThrowAsync<TrainException>().WithMessage("The card was declined.");
        catalogue.TryReserve([("TEE", 3)]).Should().BeNull("the three T-shirts are back");
    }

    [Test]
    public async Task Run_TimedOut_RethrowsAndReleasesTheStock()
    {
        var catalogue = ReservedCatalogue();
        var payments = new ScriptedPaymentProvider(new TimeoutException("No answer."));

        var act = () =>
            Junction(payments, catalogue).Run((Order, Cart, new StockReservation(Guid.NewGuid())));

        await act.Should().ThrowExactlyAsync<TimeoutException>();
        catalogue.TryReserve([("TEE", 3)]).Should().BeNull("the three T-shirts are back");
    }
}
```

The classifier is a function from an exception to a class, so its test is a table.

`tests/Shopfront.Tests/UnitTests/ShopfrontFailureClassifierTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/ShopfrontFailureClassifierTests.cs
using Shopfront.Failures;
using Shopfront.Payments;
using Trax.Core.Exceptions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class ShopfrontFailureClassifierTests
{
    private static IEnumerable<TestCaseData> Cases()
    {
        yield return new TestCaseData(new TrainException("CAP is out of stock."), FailureClass.Permanent);
        yield return new TestCaseData(new TimeoutException(), FailureClass.Transient);
        yield return new TestCaseData(new PaymentDeclinedException("Declined."), null);
        yield return new TestCaseData(new InvalidOperationException(), null);
    }

    [TestCaseSource(nameof(Cases))]
    public void Classify_Exception_GivesItsClass(Exception exception, FailureClass? expected) =>
        new ShopfrontFailureClassifier().Classify(exception).Should().Be(expected);
}
```

A `PaymentDeclinedException` never reaches the classifier, because the junction turns it into a
`TrainException` first; the test pins that the classifier has no opinion of its own about it.

The train test gains the classifier and a test for each way the payment fails.

`tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/PlaceOrderTrainTests.cs
using Microsoft.Extensions.DependencyInjection;
using Shopfront.Failures;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.FailureClassifier;
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
        services.AddSingleton<IFailureClassifier, ShopfrontFailureClassifier>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
                .AddMediator(typeof(IPlaceOrderTrain).Assembly)
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public ValueTask DisposeTheContainer() => _provider.DisposeAsync();

    private static PlaceOrderInput Order(string customerId, string paymentToken, string sku) =>
        new()
        {
            CustomerId = customerId,
            Lines = [new OrderLine(sku, 1)],
            PaymentToken = paymentToken,
        };

    [Test]
    public async Task RunAsync_InStock_ConfirmsTheOrderAndMasksTheReceipt()
    {
        // A customer no other test uses, to find this run's record among any others.
        var customerId = Guid.NewGuid().ToString();
        var bus = _provider.GetRequiredService<ITrainBus>();

        var output = await bus.RunAsync<PlaceOrderOutput>(Order(customerId, "tok_visa", "MUG"));

        output.ReceiptUrl.Should().StartWith("https://pay.example/receipts/");
        var run = RunFor(customerId);
        run.TrainState.Should().Be(TrainState.Completed);
        run.Output.Should().Contain(output.TransactionId).And.NotContain(output.ReceiptUrl);
    }

    [Test]
    public async Task RunAsync_OutOfStock_FailsAtReserveStockAsPermanent()
    {
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
    public async Task RunAsync_Declined_FailsAtTakePaymentAsPermanent()
    {
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
    }

    [Test]
    public async Task RunAsync_FifthCharge_TimesOutAsTransient()
    {
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

The host tests now start an application that records into Postgres, so they need the database
from the top of this chapter running. They send a payment token as well.

`tests/Shopfront.Tests/IntegrationTests/HostTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/HostTests.cs
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Trains.PlaceOrder;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class HostTests
{
    private WebApplicationFactory<Program> _app = null!;

    [SetUp]
    public void StartTheApplication() => _app = new WebApplicationFactory<Program>();

    [TearDown]
    public ValueTask StopTheApplication() => _app.DisposeAsync();

    private static object Order(string sku, string paymentToken = "tok_visa") =>
        new
        {
            customerId = "ada",
            paymentToken,
            lines = new[] { new { sku, quantity = 1 } },
        };

    [Test]
    public async Task PostOrders_InStock_Answers200WithTheOrder()
    {
        var response = await _app.CreateClient().PostAsJsonAsync("/orders", Order("MUG"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await response.Content.ReadFromJsonAsync<PlaceOrderOutput>();
        order!.Total.Should().Be(12.00m);
    }

    [Test]
    public async Task PostOrders_OutOfStock_Answers422WithTheReason()
    {
        var response = await _app.CreateClient().PostAsJsonAsync("/orders", Order("CAP"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("CAP is out of stock.");
    }

    [Test]
    public async Task PostOrders_Declined_Answers422WithTheReason()
    {
        var response = await _app.CreateClient()
            .PostAsJsonAsync("/orders", Order("MUG", "tok_chargeDeclined"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<string>()).Should().Be("The card was declined.");
    }
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    25, Skipped:     0, Total:    25, Duration: 1 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Every run of `PlaceOrder` is a row in Postgres with its input, its output and, when it fails, the
junction that failed, the reason and whether trying again could help. A failed payment gives its
stock back, and the receipt link never reaches the record.

The stock itself is still a dictionary in memory, and the order is confirmed without being stored
anywhere but in Trax's record of the run. The next chapter gives Shopfront a database of its own:
products, their stock and the orders placed against them.

## SDK Reference

> [UsePostgres](/docs/sdk-reference/configuration/use-postgres) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) | [TraxSensitive](/docs/sdk-reference/attributes/trax-sensitive) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Metadata](/docs/effect/metadata)
