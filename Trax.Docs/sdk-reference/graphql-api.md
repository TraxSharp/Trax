---
layout: default
title: GraphQL API
parent: SDK Reference
nav_order: 8
has_children: true
---

# GraphQL API

Trax exposes a GraphQL endpoint powered by [HotChocolate](https://chillicream.com/docs/hotchocolate). It provides queries for inspecting trains, manifests, manifest groups, and executions, mutations for queuing/running trains and managing the scheduler, and real-time subscriptions for train lifecycle events via WebSocket.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(Program).Assembly)
);

builder.Services.AddTraxGraphQL();  // Requires AddTrax() first — throws InvalidOperationException otherwise

var app = builder.Build();

app.UseTraxGraphQL(); // default: /trax/graphql (named schema — won't conflict with your own)

app.Run();
```

Navigate to the endpoint URL in a browser to open [Banana Cake Pop](https://chillicream.com/docs/bananacakepop), the built-in GraphQL IDE. It provides schema exploration, autocompletion, and query execution without any extra tooling.

## Pages

| Page | Description |
|------|-------------|
| [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | Registration and endpoint mapping |
| [TraxQuery & TraxMutation Attributes](/docs/sdk-reference/graphql-api/trax-graphql-attribute) | Opt trains into the typed GraphQL schema with `[TraxQuery]` or `[TraxMutation]` |
| [Queries](/docs/sdk-reference/graphql-api/queries) | `health`, `trains`, `manifests`, `manifest`, `manifestGroups`, `executions`, `execution` |
| [Mutations](/docs/sdk-reference/graphql-api/mutations) | Grouped into `dispatch` (auto-generated train mutations) and `operations` (scheduler control mutations) |
| [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions) | Real-time WebSocket events for train lifecycle transitions (`onTrainStarted`, `onTrainCompleted`, `onTrainFailed`, `onTrainCancelled`) |
| [TraxBroadcast Attribute](/docs/sdk-reference/graphql-api/trax-broadcast-attribute) | Opt trains into subscription events with `[TraxBroadcast]` |

## Package

```
dotnet add package Trax.Api.GraphQL
```
