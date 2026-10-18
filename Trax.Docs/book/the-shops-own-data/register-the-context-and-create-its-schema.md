---
title: "4.7 Register the context and create its schema"
description: "Chapter 4 of the Trax book, section 7: Register the context and create its schema."
type: tutorial
area: general
parent: "4. The shop's own data"
grand_parent: The Trax book
nav_order: 7
snippet_context: none
follows: book/the-shops-own-data/two-orders-one-t-shirt
---

# 4.7 Register the context and create its schema

`Program.cs`:

```csharp compile=app file=Program.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Failures;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Trains.PlaceOrder;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.FailureClassifier;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Shopfront")
    ?? throw new InvalidOperationException("ConnectionStrings:Shopfront is not set.");

builder.Services.AddDomainDataContext<IShopfrontDbContext, ShopfrontDbContext>(options =>
    options.UseNpgsql(connectionString)
);
builder.Services.AddScoped<Catalogue>();
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

`AddDomainDataContext` registers a pool of contexts and resolves `IShopfrontDbContext` from it, one
per scope. Both contexts share the one database: Trax's tables in `trax`, Shopfront's in `shop`.

`EnsureSchemaCreatedAsync` runs once the host is built. It creates the `shop` schema and any table
the model has that the database does not, with its seed rows, and leaves every existing table as it
is. That suits a first deployment. It compares tables by name only, so when a later chapter changes
a table's columns, the development database has to be recreated, or the context moved to
[EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations).

## SDK Reference

> [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context) | [EnsureSchemaCreatedAsync](/docs/effect/domain-schema-bootstrap) | [Add a domain data context](/docs/effect/effect-providers/domain-data-contexts) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Architecture guards](/docs/reference/architecture-guards)

---

Next: [4.8 Run it](/docs/book/the-shops-own-data/run-it)
