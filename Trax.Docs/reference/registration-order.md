---
layout: default
title: Registration Order
parent: Reference
nav_order: 8
---

# Registration Order

Trax's `services.AddX()` extensions are meant to be callable in any order, with one exception
that is enforced loudly. Where order does matter, the host fails at startup with a message
naming the call to move. Nothing about registration is allowed to fail silently.

## What order does matter

| Requirement | What happens if you get it wrong |
|---|---|
| `AddTrax()` before `AddTraxGraphQL()` / `AddTraxDashboard()` | Throws immediately, naming the missing call. |
| An auth scheme (`AddTraxJwtAuth`, `AddTraxApiKeyAuth`, `AddTraxJwtDispatcher`) before `AddTraxGraphQL()` | The host refuses to start. `AddTraxGraphQL()` picks the subscription interceptor from the schemes registered by the time it runs, so a scheme added afterwards would leave subscriptions unauthenticated. |
| Trains registered before `AddTraxGraphQL()` | Trains registered afterwards are not in the schema. |

```csharp
builder.Services.AddTrax(trax => trax.AddEffects(...).AddMediator(...));
builder.Services.AddTraxJwtAuth(...);
builder.Services.AddTraxGraphQL(graphql => graphql.AddDbContext<AppDbContext>());
```

## What order does not matter

Everything else, and deliberately so:

- `@authorize` from `[TraxAuthorize]` is attached to the schema, so query and mutation gating
  behaves identically whichever way round the host is composed.
- Services the GraphQL components depend on are resolved on first use, not when
  `AddTraxGraphQL()` runs, so `AddAuthentication()` and `AddAuthorization()` may come after it.
- Repeated `AddTraxJwtAuth(...)` calls accumulate into one registry regardless of order.

## Contributing to Trax

Reading the `IServiceCollection` inside a registration extension answers "is this registered
*yet*", not "will this be registered". The extension runs partway through the host's startup
code, so any behaviour derived from the answer changes with the caller's ordering.

That has shipped as a bug twice: once leaving an interceptor unable to activate (a 500 on every
request), and once leaving subscription auth unwired entirely, which accepted every connection
anonymously while HTTP kept working.

Inspecting the collection is not banned. Silence is. A site is acceptable only if it is:

1. **An idempotency guard**, asking "have I already registered my own thing?" It cannot change
   consumer-visible behaviour.
2. **A precondition that throws**, where ordering matters and the consumer is told how to fix it.
3. **A decision paired with a startup validator**: wire what you can, then assert from an
   `IHostedService`, where the container is complete, that the result is coherent, and throw
   naming the call to move.

Prefer restructuring so the question disappears. Registering a factory that resolves on first
use makes the ordering question moot rather than merely detected.

`NoSilentRegistrationOrderDependenceTests` fails the build on any new introspection site.
Making one safe and recording why is the way past it; raising its count is not.

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) | [Architecture guards](/docs/reference/architecture-guards)
