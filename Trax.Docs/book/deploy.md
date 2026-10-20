---
title: "18. Deploy"
description: "Chapter 18 of the Trax book: run the hub and its workers in containers on one Postgres and RabbitMQ, with migrations and health checks."
type: tutorial
area: general
parent: The Trax book
nav_order: 28
---

# 18. Deploy

Shopfront has run on one machine, in Development, from `dotnet run`. In this chapter it runs as it
would deployed: a hub and two workers in containers, one Postgres, and RabbitMQ between them. Four
things change before it can: the shop's schema is migrated rather than created, the processes tell
each other about runs, each says whether it is healthy, and the hub runs in Production, which
turns off everything that was there for Development.

This chapter starts from the end of [chapter 17](/docs/book/require-persisted-operations).

## In this chapter

1. [The shape](/docs/book/deploy/the-shape)
2. [Migrate the shop's schema](/docs/book/deploy/migrate-the-shops-schema)
3. [The hub](/docs/book/deploy/the-hub)
4. [The worker](/docs/book/deploy/the-worker)
5. [The tests migrate too](/docs/book/deploy/the-tests-migrate-too)
6. [Containers](/docs/book/deploy/containers)
7. [Run it as deployed](/docs/book/deploy/run-it-as-deployed)
8. [The storefront](/docs/book/deploy/the-storefront)
9. [The console](/docs/book/deploy/the-console)
10. [Before it takes real orders](/docs/book/deploy/before-it-takes-real-orders)

## SDK Reference

> [Deployment topologies](/docs/reference/deployment-topologies) | [Move a domain data context to EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq) | [AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check) | [Health and liveness](/docs/scheduler/health-and-liveness) | [Startup refusals](/docs/security/startup-refusals) | [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard)

---

Next: [18.1 The shape](/docs/book/deploy/the-shape)
