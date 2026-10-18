---
title: "7.4 Serve the schema"
description: "Chapter 7 of the Trax book, section 4: Serve the schema."
type: tutorial
area: general
parent: "7. Serve it over GraphQL"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/serve-it-over-graphql/the-catalogue-and-the-orders-become-queries
---

# 7.4 Serve the schema

`Program.cs`:

```csharp compile=app file=Program.cs
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Extensions;
using Trax.Api.Auth.Jwt;
using Trax.Api.GraphQL.Extensions;
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

if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxJwtAuth(jwt =>
        jwt.UseSymmetricKey(DevJwt.Issuer, DevJwt.Audience, DevJwt.Key)
    );
else
    builder.Services.AddTraxJwtAuth(
        builder.Configuration["Auth:Authority"]
            ?? throw new InvalidOperationException("Auth:Authority is not set."),
        builder.Configuration["Auth:Audience"]
            ?? throw new InvalidOperationException("Auth:Audience is not set.")
    );
builder.Services.AddAuthorization();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .SaveTrainParameters()
                .AddJunctionLogger(serializeJunctionData: true)
                .AddDecisionRecording()
                .AddJunctionEvents()
        )
        .AddMediator(typeof(Program).Assembly)
);
builder.Services.AddTraxGraphQL(graphql => graphql.AddDbContext<ShopfrontDbContext>());

var app = builder.Build();

await app.Services.EnsureSchemaCreatedAsync<ShopfrontDbContext>();

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();

if (app.Environment.IsDevelopment())
    app.MapGet(
            "/dev/token/{user}",
            (string user, bool staff = false) =>
                DevJwt.Mint(user, staff ? Roles.Staff : Roles.Customer)
        )
        .AllowAnonymous();

await app.RunAsync();
```

The two hand-written endpoints are gone. What changed:

- **`AddTraxGraphQL`** builds the schema from every train with a GraphQL attribute and every query
  model on the contexts it is given. It comes after `AddTrax`, because the trains have to be
  registered before the schema can be built from them. It also registers what `AddTraxApi` did,
  including the service that checks `[TraxAuthorize]`, which every field of the schema goes through.
- **`AddDbContext<ShopfrontDbContext>()`** names the context the query models read from. It resolves
  the context per request, built for that request's caller, so the query filter applies.
- **`AddJunctionEvents()`** records each junction of a run as a step, which is what a customer
  following a run sees.
- **`UseTraxGraphQL()`** serves the schema at `/trax/graphql`.

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | [Query models](/docs/sdk-reference/graphql-api/query-models) | [Run progress](/docs/sdk-reference/graphql-api/run-progress) | [AddJunctionEvents](/docs/sdk-reference/configuration/add-junction-events) | [dispatch mutations](/docs/sdk-reference/graphql-api/mutations/dispatch)

---

Next: [7.5 Ask it](/docs/book/serve-it-over-graphql/ask-it)
