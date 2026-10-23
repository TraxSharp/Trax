---
title: UseTraxOperatorConsoleDocuments
description: "Reference for UseTraxOperatorConsoleDocuments, which registers the operations console's documents on a host that serves no dashboard page, for trax-console."
type: reference
area: dashboard
package: Trax.Api.Dashboard
namespace: Trax.Api.Dashboard.Extensions
parent: API Dashboard
grand_parent: SDK Reference
nav_order: 2
source:
  - Trax.Api/src/Trax.Api.Dashboard/Extensions/ApiDashboardEndpointExtensions.cs
---

# UseTraxOperatorConsoleDocuments

Registers the documents the operations console sends, the same ones
[`UseTraxApiDashboard`](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) registers, without serving the
dashboard. It is for a host that requires persisted operations and that operators reach only from a terminal, with
`trax-console`. A host that serves the dashboard needs no call to it.

```csharp compile
using Trax.Api.Dashboard.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;

builder.Services.AddTraxGraphQL(graphql => graphql
    .ExposeOperationQueries()
    .ExposeOperationMutations()
    .GateOperations(roles: "Operator")
    .UsePersistedOperations(po => po.RequirePersisted(true)));

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();
app.UseTraxOperatorConsoleDocuments();
```

## Signature

```csharp fragment
public static IEndpointRouteBuilder UseTraxOperatorConsoleDocuments(this IEndpointRouteBuilder endpoints)
```

It returns `endpoints`, for chaining.

## Behavior

- **What it registers.** The console's documents, embedded in the package, each under
  `trax_console_<name>_<hash>` when the host starts, as `UseTraxApiDashboard` does
  ([Trax.Api ADR 0042](https://github.com/TraxSharp/Trax/blob/main/Trax.Api/docs/adr/0042-the-operations-console-is-registered-as-persisted-documents.md)).
  A document the host cannot serve is left out and logged.
- **What it does not change.** Who may run them. Every request is still decided by the operations gate
  (`GateOperations(...)` or `GateOperationsToAuthenticatedUsers()`), or, for a stream (`onTrainStateChanged`, `onDataChanged`,
  `onJunctionEvent`, `onMachineInstanceChanged`), by its per-subscriber authorization, which delivers each event only
  to a caller who may see it. No page, route or anonymous access is added.
- **Without persisted operations** nothing is registered, and a frontend sends the documents' text.
- **Call it before the host starts.** Calling it again, or beside `UseTraxApiDashboard`, registers each document once.
  A call after the host has started and registered its persisted documents throws, since the documents would never be
  registered.

## Exceptions

| Exception | When |
|-----------|------|
| `InvalidOperationException` | `AddTraxGraphQL()` was not called, or does not expose the operations namespace (`ExposeOperationQueries()`). |
| `InvalidOperationException` | The host has already started and registered its persisted documents. |
