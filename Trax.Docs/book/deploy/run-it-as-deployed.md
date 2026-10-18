---
title: "18.7 Run it as deployed"
description: "Chapter 18 of the Trax book, section 7: Run it as deployed."
type: tutorial
area: general
parent: "18. Deploy"
grand_parent: The Trax book
nav_order: 7
snippet_context: none
follows: book/deploy/containers
---

# 18.7 Run it as deployed

`compose.yaml` runs the whole of it: Postgres, RabbitMQ, the hub and two workers.

`compose.yaml`:

```yaml file=compose.yaml
# Shopfront as it runs deployed: one hub, two workers, Postgres and RabbitMQ. Every secret comes
# from the environment, or from a .env file beside this one that is never committed.

# What the hub and the workers both need.
x-shopfront: &shopfront
  ConnectionStrings__Shopfront: Host=database;Database=shopfront;Username=shopfront;Password=${POSTGRES_PASSWORD}
  ConnectionStrings__RabbitMq: amqp://shopfront:${RABBITMQ_PASSWORD}@rabbitmq:5672

services:
  database:
    image: postgres:17
    environment:
      POSTGRES_DB: shopfront
      POSTGRES_USER: shopfront
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD}
    volumes:
      - database:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD", "pg_isready", "-U", "shopfront"]
      interval: 5s

  rabbitmq:
    image: rabbitmq:4
    environment:
      RABBITMQ_DEFAULT_USER: shopfront
      RABBITMQ_DEFAULT_PASS: ${RABBITMQ_PASSWORD:?set RABBITMQ_PASSWORD}
    healthcheck:
      test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
      interval: 5s

  hub:
    build:
      context: .
      dockerfile: Shopfront.Hub/Dockerfile
    environment:
      <<: *shopfront
      # The identity provider whose tokens the hub accepts, and where the storefront is served.
      Auth__Authority: ${AUTH_AUTHORITY:?set AUTH_AUTHORITY}
      Auth__Audience: ${AUTH_AUDIENCE:?set AUTH_AUDIENCE}
      Storefront__Origin: ${STOREFRONT_ORIGIN:?set STOREFRONT_ORIGIN}
    ports:
      - "8080:8080"
    depends_on:
      database:
        condition: service_healthy
      rabbitmq:
        condition: service_healthy

  worker:
    build:
      context: .
      dockerfile: Shopfront.Worker/Dockerfile
    environment: *shopfront
    deploy:
      replicas: 2
    # The hub migrates the shop's schema before it serves.
    depends_on:
      hub:
        condition: service_started

volumes:
  database:
```

A published build runs in Production unless its environment says otherwise, and nothing here says
otherwise. So:

- **The hub accepts only tokens from your identity provider**, `Auth:Authority` and
  `Auth:Audience`. The Development key is refused outside Development, and `/dev/token` is not
  mapped.
- **Only registered documents run**, and `PersistedOperations:Require` is ignored.
- **The GraphQL IDE and schema download are not served.**
- **Development's short timeouts and fast retries are off**: a run whose worker died is failed
  after more than an hour, not a minute, and the restock runs hourly.

Set the secrets, and start it:

```bash
export POSTGRES_PASSWORD=... RABBITMQ_PASSWORD=...
export AUTH_AUTHORITY=https://login.example.com/ AUTH_AUDIENCE=shopfront
export STOREFRONT_ORIGIN=https://shop.example.com
docker compose up -d --build
```

```bash
curl -s http://localhost:8080/health/live
curl -s http://localhost:8080/health/ready
```

```text
Healthy
Healthy
```

The hub migrated both schemas and serves:

```text
info: Microsoft.EntityFrameworkCore.Migrations[20402]
      Applying migration '20261018214548_Initial'.
...
info: Trax.Effect.Broadcaster.RabbitMQ.RabbitMqTrainEventReceiver[0]
      RabbitMQ receiver started on exchange trax.lifecycle, queue amq.gen-iiJgCfj_9Vb3N9nM6CyCoA.
info: Trax.Scheduler.Services.SchedulerStartupService.SchedulerStartupService[0]
      Successfully seeded 3 manifest(s)
info: Trax.Api.GraphQL.PersistedOperations[0]
      Persisted-operation enforcement is ON. ...
```

EF Core logs one `fail:` before that, the first time: it looks for the history table before it
creates it. On a fresh database that is expected, and later starts log none.

The storefront's documents run by id, and text is refused, as in chapter 17. The catalogue has more
caps and T-shirts than it started with: the restock ran as soon as the hub scheduled it, on a
worker, which logged `Received 2 deliveries`.

```text
{"data":{"discover":{"products":{"nodes":[{"sku":"CAP","name":"Cap","price":18.50,"stock":5},{"sku":"MUG","name":"Mug","price":12.00,"stock":10},{"sku":"TEE","name":"T-shirt","price":25.00,"stock":8}]}}}} 200
{"errors":[{"message":"Only persisted operations are accepted on this server.","extensions":{"code":"PERSISTED_OPERATION_REQUIRED"}}]} 400
```

`/dev/token` and the GraphQL IDE answer `404`. The hub also warns that ASP.NET Core keeps its
data-protection keys inside the container. Shopfront signs nothing with them, since its callers
carry bearer tokens and no cookies; a hub that adds cookie sign-in keeps those keys somewhere that
outlives the container.

## SDK Reference

> [Deployment topologies](/docs/reference/deployment-topologies) | [Move a domain data context to EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq) | [AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check) | [Health and liveness](/docs/scheduler/health-and-liveness) | [Startup refusals](/docs/security/startup-refusals) | [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard)

---

Next: [18.8 The storefront](/docs/book/deploy/the-storefront)
