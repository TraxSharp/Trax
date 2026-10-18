---
title: "8.3 Open the namespace to staff, and serve the console"
description: "Chapter 8 of the Trax book, section 3: Open the namespace to staff, and serve the console."
type: tutorial
area: general
parent: "8. The operations console"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/the-operations-console/packages
---

# 8.3 Open the namespace to staff, and serve the console

`Program.cs`:

```csharp compile=app file=Program.cs
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Extensions;
using Trax.Api.Auth.Jwt;
using Trax.Api.Dashboard.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

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
        .AddScheduler(scheduler => scheduler)
);
builder.Services.AddTraxGraphQL(graphql =>
    graphql
        .AddDbContext<ShopfrontDbContext>()
        .ExposeOperationQueries()
        .ExposeOperationMutations()
        .GateOperations(roles: Roles.Staff)
);

var app = builder.Build();

await app.Services.EnsureSchemaCreatedAsync<ShopfrontDbContext>();

app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.UseTraxApiDashboard(dashboard => dashboard.AllowAnonymousShell());

if (app.Environment.IsDevelopment())
    app.MapGet(
            "/dev/token/{user}",
            (string user, bool staff = false) =>
                DevJwt.Mint(user, staff ? Roles.Staff : Roles.Customer)
        )
        .AllowAnonymous();

await app.RunAsync();
```

What changed:

- **`AddScheduler`** comes straight after `AddMediator`. It registers the operations service every
  `operations` field reads through. With nothing scheduled it schedules nothing; it also starts a
  worker that runs queued trains, which nothing queues yet.
- **`ExposeOperationQueries` and `ExposeOperationMutations`** add the `operations` namespace: the
  runs, the trains, the hosts, and the actions an operator takes, such as cancelling or
  re-queueing a run. Both are off by default, because they reveal and control the whole
  deployment.
- **`GateOperations(roles: Roles.Staff)`** is the gate. Every field under `operations` requires the
  `Staff` role, on every request and on every subscription. Leave the gate out and the host
  refuses to start, naming the three ways to answer it.
- **`UseTraxApiDashboard`** serves the console at `/trax/dashboard`. It also refuses to start
  until it is told who may load the page.

## SDK Reference

> [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Queries](/docs/sdk-reference/graphql-api/queries)

---

Next: [8.4 Who may load the page](/docs/book/the-operations-console/who-may-load-the-page)
