---
title: "19. Deploy"
description: "Chapter 19 of the Trax book: deploy the hub and its workers with trax deploy, on this machine and then on a cluster, with migrations, drains and health checks."
type: tutorial
area: general
parent: The Trax book
nav_order: 29
---

# 19. Deploy

Shopfront has run on one machine, in Development, from `dotnet run`. In this chapter `trax deploy`
runs it as it would deployed: a hub and its workers in hardened containers, one Postgres, and
RabbitMQ between them. A few things change in the code first: the shop's schema is migrated rather
than created, by a step of the deploy; the processes tell each other about runs; each says whether
it is healthy; each can report what it needs from its deployment; and the hub runs in Production,
which turns off everything that was there for Development. Then one file describes the deployment,
and the same file runs Shopfront on this machine and on a Kubernetes cluster, draining before every
release, holding each process to what it may reach, and on the cluster rolling a release back that
cannot start.

This chapter starts from the end of [chapter 18](/docs/book/require-persisted-operations).

## In this chapter

1. [The shape](/docs/book/deploy/the-shape)
2. [Migrate the shop's schema](/docs/book/deploy/migrate-the-shops-schema)
3. [The hub](/docs/book/deploy/the-hub)
4. [The worker](/docs/book/deploy/the-worker)
5. [The tests migrate too](/docs/book/deploy/the-tests-migrate-too)
6. [Describe the deployment](/docs/book/deploy/describe-the-deployment)
7. [Run it as deployed](/docs/book/deploy/run-it-as-deployed)
8. [The storefront](/docs/book/deploy/the-storefront)
9. [The console](/docs/book/deploy/the-console)
10. [Before it takes real orders](/docs/book/deploy/before-it-takes-real-orders)

## SDK Reference

> [Deployment topologies](/docs/reference/deployment-topologies) | [Move a domain data context to EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq) | [AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check) | [Health and liveness](/docs/scheduler/health-and-liveness) | [Startup refusals](/docs/security/startup-refusals) | [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) | [DeployManifest](/docs/sdk-reference/configuration/deploy-manifest) | [TraxMigrate](/docs/sdk-reference/configuration/trax-migrate) | [TraxDrain](/docs/sdk-reference/configuration/trax-drain)

---

Next: [19.1 The shape](/docs/book/deploy/the-shape)
