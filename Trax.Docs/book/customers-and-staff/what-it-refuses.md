---
title: "6.8 What it refuses"
description: "Chapter 6 of the Trax book, section 8: What it refuses."
type: tutorial
area: general
parent: "6. Customers and staff"
grand_parent: The Trax book
nav_order: 8
snippet_context: none
follows: book/customers-and-staff/try-it
---

# 6.8 What it refuses

Comment out `builder.Services.AddTraxApi();` and start the application:

```text
fail: Microsoft.Extensions.Hosting.Internal.Host[11]
      Hosting failed to start
      System.InvalidOperationException: One or more registered trains declare [TraxAuthorize] but no ITrainAuthorizationService is registered in the DI container. Call services.AddTraxApi() (or register a custom ITrainAuthorizationService) before building the host. If this process intentionally runs no API submissions (for example a scheduler-only worker), opt out via AddMediator(m => m.AllowMissingAuthorizationService()). Authorized trains: IPlaceOrderTrain.
```

`PlaceOrder` declares who may run it, and nothing is registered to check it, so the host refuses
to serve rather than run every train for everyone.

Put the line back. Now start the application outside Development:

```bash
ASPNETCORE_ENVIRONMENT=Production Auth__Authority=https://id.example Auth__Audience=shopfront \
  dotnet run --urls http://localhost:5080
```

It starts, and `/dev/token/ada` answers `404`: Production reads its keys from the identity
provider, never sees the development key, and has no route that mints tokens. Had the development key been registered there, the host
would have refused it by its marker.

## SDK Reference

> [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxPrincipal](/docs/security/inject-the-current-principal) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Scope a data context to the caller](/docs/effect/scope-a-data-context-to-the-caller) | [Effect data testing](/docs/sdk-reference/testing/effect-data-testing)

---

Next: [6.9 Test it](/docs/book/customers-and-staff/test-it)
