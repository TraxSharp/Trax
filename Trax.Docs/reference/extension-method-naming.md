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

Every public extension method in an `Extensions/` folder contains `Trax` in its name.
`ExtensionMethodNamingTests`, which each of the eight code repos carries, holds part of that:
it parses the `.cs` files under an `Extensions/` folder in the repo's source tree and flags a
`public static` method only when the name starts with `Add` or `Use` **and** the `this`
parameter is one of `IServiceCollection`, `IApplicationBuilder`, `IEndpointRouteBuilder`,
`WebApplication` or `WebApplicationBuilder`. A method on a Trax builder type, or one whose name
starts with anything else, is outside what it reads. Each repo's copy also carries its own
`KnownExceptions` list, so a name can be exempted with a justification rather than fixed.
Everything the check does not reach is held up by review.

The distinction matters because the two run at different times. An `Add*` method sees a
collection that is still being populated, so what it finds there answers "is this registered
*yet*", not "will this be registered". Reading the collection is not itself wrong; deriving
behaviour from the answer and saying nothing about it is.
[Registration Order](/docs/reference/registration-order) has the three shapes that are
acceptable. A `Use*` method runs after the container is built and can resolve anything, which
is usually the better place to put the question.

## SDK Reference

> [Configuration](/docs/sdk-reference/configuration) | [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard) | [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql)
