---
title: GraphQL API
description: "Index of the Trax GraphQL API reference: registration, the query, mutation and subscription types, the machines root, attributes and error codes."
type: landing
area: api
parent: SDK Reference
has_children: true
nav_order: 10
---

# GraphQL API

The Trax GraphQL endpoint, served by [HotChocolate](https://chillicream.com/docs/hotchocolate) as a
named schema. [API](/docs/api) explains the layer; these pages describe it.

```csharp compile
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(Program).Assembly)
);

builder.Services.AddTraxGraphQL();

var app = builder.Build();

app.UseTraxGraphQL(); // default: /trax/graphql

app.Run();
```

## Trax.Api.GraphQL

- [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql): registration, the builder methods, endpoint mapping.
- [Queries](/docs/sdk-reference/graphql-api/queries): the `discover` and `operations` query namespaces, one page per namespace.
- [Mutations](/docs/sdk-reference/graphql-api/mutations): the `dispatch` and `operations` mutation namespaces, one page per namespace.
- [Subscriptions](/docs/sdk-reference/graphql-api/subscriptions): lifecycle, junction, machine instance and data change events; WebSocket and authentication.
- [Machines root](/docs/sdk-reference/graphql-api/machines): each exposed state machine as its own typed operations.
- [ConfigureFiltering](/docs/sdk-reference/graphql-api/configure-filtering): opt-in filter operators.
- [Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders): batched lookups across `DbContext`s.
- [Error codes](/docs/sdk-reference/graphql-api/error-codes): every code in `extensions.code`.

## Trax.Effect

- [TraxQuery and TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute): expose a train as a query or mutation.
- [TraxBroadcast](/docs/sdk-reference/graphql-api/trax-broadcast-attribute): stream a train's lifecycle events.
- [TraxQueryModel](/docs/sdk-reference/graphql-api/query-models): expose an EF Core entity as a query.

## Trax.Api

- [AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check): the ASP.NET Core health check.
