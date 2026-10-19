---
title: API Dashboard
description: "Index of the Trax.Api.Dashboard reference: the React operations dashboard a Trax.Api host serves with UseTraxApiDashboard, and its options."
type: landing
area: dashboard
parent: SDK Reference
has_children: true
nav_order: 7.5
redirect_from:
  - /docs/sdk-reference/dashboard-api
  - /docs/sdk-reference/dashboard-api/dashboard-options
  - /docs/sdk-reference/dashboard-api/i-dashboard-settings-service
  - /docs/sdk-reference/dashboard-api/component-model-types
---

# API Dashboard

`Trax.Api.Dashboard` is the Trax operations dashboard as a single-page app (React) that a Trax.Api
host serves: runs, the work queue, dead letters, manifests and groups, logs, trains, effects,
settings, persisted operations and state machines. The built app ships inside the package, so the
host needs no Node. Everything it shows or changes goes through the host's Trax GraphQL endpoint
(the `operations` namespace and the machines root), which authorizes it on its own. A demo runs
on recorded data at [traxsharp.net/dashboard](https://traxsharp.net/dashboard).

It is the one operator dashboard Trax ships. The Blazor `Trax.Dashboard` package it replaced is
removed; [Moving from the Blazor dashboard](/docs/dashboard/add-the-console-to-a-host#moving-from-the-blazor-dashboard)
says what a host changes.

```csharp compile
using Trax.Api.Dashboard.Extensions;
using Trax.Api.GraphQL.Extensions;

builder.Services.AddTraxGraphQL(graphql => graphql.RequireAuthorization());

var app = builder.Build();
app.UseTraxGraphQL();
app.UseTraxApiDashboard(dashboard => dashboard.RequirePolicy("TraxAdmin")); // /trax/dashboard
```

| Page | Description |
|------|-------------|
| [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) | Serves the app at a path, behind the authorization posture you choose, with its security and caching headers; `TraxApiDashboardOptions` |
