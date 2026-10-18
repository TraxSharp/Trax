---
title: "5.4 Register Shopfront in one place"
description: "Chapter 5 of the Trax book, section 4: Register Shopfront in one place."
type: tutorial
area: general
parent: "5. Does this order look like fraud"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/does-this-order-look-like-fraud/the-tracks
---

# 5.4 Register Shopfront in one place

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

## SDK Reference

> [Gate](/docs/sdk-reference/train-methods/gate) | [Decide](/docs/sdk-reference/train-methods/decide) | [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [Write a decider](/docs/core/write-a-decider) | [Try a decider in shadow](/docs/core/try-a-decider-in-shadow) | [Test a train's decisions](/docs/core/test-train-decisions)

---

Next: [5.5 Run it](/docs/book/does-this-order-look-like-fraud/run-it)
