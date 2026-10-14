---
title: Broadcast events between processes
description: How to wire a hub process and its workers to one RabbitMQ exchange, so GraphQL subscriptions and SignalR clients see runs that execute on any worker.
type: how-to
area: effects
nav_order: 21
---

# Broadcast events between processes

When trains run on worker processes and clients subscribe on the hub (the API and scheduler), the hub only hears
about runs it executes itself until every process shares a broadcaster. Each process calls `UseBroadcaster` with
the same RabbitMQ connection and exchange.

## 1. Add the transport to the hub

```csharp compile=hub
using Trax.Api.GraphQL.Extensions;
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Trax")!;
var rabbitMq = builder.Configuration.GetConnectionString("RabbitMq")!;

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .UseBroadcaster(b => b.UseRabbitMq(rabbitMq))
    )
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler()
);

builder.Services.AddTraxGraphQL();

builder.Build().Run();
```

`AddTraxGraphQL()` sees the receiver `UseBroadcaster` registered and forwards what arrives to subscriptions. No other
setting is needed.

## 2. Add the same transport to each worker

```csharp compile=worker
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Trax")!;
var rabbitMq = builder.Configuration.GetConnectionString("RabbitMq")!;

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .UseBroadcaster(b => b.UseRabbitMq(rabbitMq))
    )
    .AddMediator(typeof(Program).Assembly)
);

builder.Services.AddTraxWorker(opts => opts.WorkerCount = 4);

builder.Build().Run();
```

## 3. Check every host connected

A host that cannot reach the broker, or that the broker refuses (a wrong user or password), still starts and runs its
trains, and keeps retrying; nothing crosses between processes in the meantime. Look for
`RabbitMQ receiver started on exchange trax.lifecycle` in each host's startup log, and give every host a user the
broker has.

## 4. Give each application its own exchange

Every process bound to an exchange receives everything published to it. When two applications share a broker, set a
distinct `ExchangeName` (or use a vhost each):

```csharp fragment
.UseBroadcaster(b => b.UseRabbitMq(rabbitMq, opts => opts.ExchangeName = "billing.lifecycle"))
```

## See also

- [How event broadcasting works](/docs/effect/how-event-broadcasting-works)
- [Push train events to browsers](/docs/effect/push-train-events-to-browsers)

## SDK Reference

> [UseBroadcaster](/docs/sdk-reference/configuration/use-broadcaster) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker)
