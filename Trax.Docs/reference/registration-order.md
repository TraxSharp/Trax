---
layout: default
title: Registration Order
description: Which Trax registration calls must come in a set order, the startup error each wrong order raises, and which orderings deliberately do not matter.
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
| An authorization posture in `AddTraxDashboard()` options before `UseTraxDashboard()` | `UseTraxDashboard()` throws, naming `RequirePolicy`, `RequireRoles` and `AllowAnonymousDashboard`. |
| Trains registered before `AddTraxGraphQL()` | The host refuses to start, naming each `[TraxQuery]`, `[TraxMutation]` or `[TraxBroadcast]` train registered afterwards. `AddTraxGraphQL()` checks each train's authorization posture and name and builds the schema roots from the trains registered before it. |
| Your own `IDecisionObserver` before `AddTrax()`, on a host that calls `AddDecisionRecording()` | The host refuses to start, naming the observer, and every run that would record its decisions refuses too. One registered after `AddTrax()` would replace the composite that tells decision recording. See [Other decision observers](/docs/effect/decisions#other-decision-observers). |

```csharp
builder.Services.AddTrax(trax => trax.AddEffects(...).AddMediator(...));
builder.Services.AddTraxJwtAuth(...);
builder.Services.AddTraxGraphQL(graphql => graphql.AddDbContext<AppDbContext>());
```

## Missing registrations and what they raise

These are not about order: the call is missing. Each one fails at startup, with the message shown.

| Missing | Message |
|---|---|
| `AddScheduler()` on a host that calls `UseTraxDashboard()` | `UseTraxDashboard() requires the Trax Scheduler: the dashboard queues, runs and cancels trains through IOperationsService, which AddScheduler() registers.` |
| A data provider (`UseInMemory()`, `UseSqlite()`, `UsePostgres()`) in `AddEffects` on a host that calls `AddScheduler()` | `AddScheduler() requires a data provider (UsePostgres(), UseSqlite(), or UseInMemory()).` |
| The train's assembly in `AddMediator(...)`, for a train named in `Schedule<T>` | `No train implements IServiceTrain<TInput, TOut>. Add the train's assembly to AddMediator(m => m.ScanAssemblies(typeof(MyTrain).Assembly)).` |
| `[TraxAuthorize]` or `[TraxAllowAnonymous]` on a `[TraxQuery]` or `[TraxMutation]` train | `Trax GraphQL exposure authorization check failed:` followed by one line per train |
| `AddTraxDashboard()` on a host that calls `UseTraxDashboard()` | `UseTraxDashboard() requires AddTraxDashboard() to be called first.` |
| `AddTraxGraphQL()` on a host that calls `UseTraxGraphQL()` | `UseTraxGraphQL() requires AddTraxGraphQL() to be called first.` |
| `AddAuthentication()` on a host that calls `UseAuthentication()` with no auth package registered (a template outside Development, where the demo key is not registered) | `Unable to resolve service for type 'Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider'` |
| A registered demo key outside Development: `AddTraxApiKeyAuth` with a key containing `do-not-use-in-production` | `AddTraxApiKeyAuth() registered a key containing 'do-not-use-in-production', which marks a published demo key, and the environment is 'Production'.` |
| A demo signing key outside Development: `AddTraxJwtAuth` with a symmetric key containing `do-not-use-in-production` | `AddTraxJwtAuth() registered a signing key containing 'do-not-use-in-production', which marks a published demo key, and the environment is 'Production'.` |
| An authentication scheme on a host whose dashboard uses `RequirePolicy(...)` or `RequireRoles(...)` | `The dashboard requires authorization, but the host has no authentication scheme to challenge a caller with` |
| The type an `[ExtendObjectType("...")]` class extends, such as `Subscription` where Trax's root is `LifecycleSubscriptions` | `Type extension '...' extends 'Subscription', which is not a type in this schema` |

Without an authentication scheme, a dashboard policy would have nobody to challenge, and every
request it refused would be a 500 rather than a 401 or 403. That is why the dashboard refuses to
start instead. Register the scheme your users sign in with before choosing a policy.

A complete host that registers all of these in a working order is the
[`trax-hub` template](/docs/reference/templates): its `Program.cs` comments each call with the
message its removal raises.

## What order does not matter

Everything else, and deliberately so:

- `@authorize` from `[TraxAuthorize]` is attached to the schema, so query and mutation gating
  behaves identically whichever way round the host is composed.
- Services the GraphQL components depend on are resolved on first use, not when
  `AddTraxGraphQL()` runs, so `AddAuthentication()` and `AddAuthorization()` may come after it.
- Repeated `AddTraxJwtAuth(...)` calls accumulate into one registry regardless of order.
- Auth schemes (`AddTraxJwtAuth`, `AddTraxApiKeyAuth`, `AddTraxJwtDispatcher`) may come before or
  after `AddTraxGraphQL()`. The subscription interceptor reads them from the finished container
  on the first connection.
- `app.UseTraxGraphQL()` before or after `app.UseAuthentication()` and `app.UseAuthorization()`.
  It maps an endpoint, and a `WebApplication` runs endpoints after all of its middleware, so the
  caller is authenticated either way. Calling the two `Use` methods first is still the
  conventional order, and the one the templates use.

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
