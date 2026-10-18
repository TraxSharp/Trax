---
title: "6.6 Sign in, and run trains as the caller"
description: "Chapter 6 of the Trax book, section 6: Sign in, and run trains as the caller."
type: tutorial
area: general
parent: "6. Customers and staff"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/customers-and-staff/a-customer-reads-their-own-orders
---

# 6.6 Sign in, and run trains as the caller

`Program.cs`:

```csharp compile=app file=Program.cs
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Extensions;
using Shopfront.Trains.PlaceOrder;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.Exceptions;
using Trax.Api.Extensions;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainExecution;

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
        )
        .AddMediator(typeof(Program).Assembly)
);
builder.Services.AddTraxApi();

var app = builder.Build();

await app.Services.EnsureSchemaCreatedAsync<ShopfrontDbContext>();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost(
        "/orders",
        async (PlaceOrderInput input, ITrainExecutionService trains, CancellationToken ct) =>
        {
            try
            {
                var run = await trains.RunAsync(
                    typeof(IPlaceOrderTrain).FullName!,
                    JsonSerializer.Serialize(input),
                    ct
                );
                return Results.Ok(run.Output);
            }
            catch (TrainAuthorizationException)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }
            catch (TrainException ex)
            {
                return Results.UnprocessableEntity(ex.Message);
            }
        }
    )
    .RequireAuthorization(TraxAuthClaimTypes.TraxAuthPolicy);

app.MapGet(
        "/orders",
        async (IShopfrontDbContext db, CancellationToken ct) =>
            await db
                .Orders.OrderBy(o => o.PlacedAt)
                .Select(o => new
                {
                    o.Id,
                    o.CustomerId,
                    o.Total,
                    o.PlacedAt,
                })
                .ToListAsync(ct)
    )
    .RequireAuthorization(TraxAuthClaimTypes.TraxAuthPolicy);

if (app.Environment.IsDevelopment())
    app.MapGet(
            "/dev/token/{user}",
            (string user, bool staff = false) =>
                DevJwt.Mint(user, staff ? Roles.Staff : Roles.Customer)
        )
        .AllowAnonymous();

await app.RunAsync();
```

From the top:

- **`AddTraxJwtAuth`** registers the JWT scheme as `TraxJwt`, and the services that turn a signed-in
  caller into `TraxPrincipal` and `TraxCaller`. In Development it checks tokens against the
  development key; anywhere else against an identity provider, read from configuration. Nothing
  else changes between the two, because the rest of the application sees only the principal.
- **`AddTraxApi`** registers the service that checks `[TraxAuthorize]`. Leave it out and the host
  does not start; that is below.
- **`RequireAuthorization(TraxAuthClaimTypes.TraxAuthPolicy)`** on each endpoint signs the caller
  in with Trax's schemes and refuses anyone who is not, with a `401`. The Trax scheme is never the
  application's default, so an endpoint that says nothing signs nobody in.
- **`POST /orders`** runs `PlaceOrder` through `ITrainExecutionService`, by its interface's name and
  with its input as JSON. That is the path that checks `[TraxAuthorize]`: a member of staff is
  signed in, so passes the endpoint, and is refused by the train with a `403`.
- **`GET /orders`** queries the orders. It has no `Where`: the query filter is the rule.
- **`/dev/token/{user}`** mints a token, in Development only. Outside it, the route does not exist.

## SDK Reference

> [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxPrincipal](/docs/security/inject-the-current-principal) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Scope a data context to the caller](/docs/effect/scope-a-data-context-to-the-caller) | [Effect data testing](/docs/sdk-reference/testing/effect-data-testing)

---

Next: [6.7 Try it](/docs/book/customers-and-staff/try-it)
