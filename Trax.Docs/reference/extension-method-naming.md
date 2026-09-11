---
layout: default
title: Extension Method Naming
parent: Reference
nav_order: 10
---

# Extension Method Naming

`services.AddTrax(trax => trax.AddEffects(...).AddMediator(...).AddScheduler(...))` is the
unified entry point. Everything else follows one of two prefixes.

| Prefix | Means | Examples |
|---|---|---|
| `Add*` | Registers services on the `IServiceCollection` | `AddTraxDashboard`, `AddTraxGraphQL`, `AddTraxWorker`, `AddTraxJobRunner` |
| `Use*` | Maps endpoints or components onto a built `WebApplication` | `UseTraxDashboard`, `UseTraxGraphQL` |

Every public extension method in an `Extensions/` folder contains `Trax` in its name, which
`ExtensionMethodNamingTests` enforces in each repo.

The distinction matters because the two run at different times. An `Add*` method sees a
collection that is still being populated, which is why reading it is a trap (see
[Registration Order](/docs/reference/registration-order)). A `Use*` method runs after the
container is built and can resolve anything.

## SDK Reference

> [Configuration](/docs/sdk-reference/configuration) | [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard) | [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)
