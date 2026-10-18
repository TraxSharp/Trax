---
title: "18.1 The shape"
description: "Chapter 18 of the Trax book, section 1: The shape."
type: tutorial
area: general
parent: "18. Deploy"
grand_parent: The Trax book
nav_order: 1
snippet_context: none
follows: book/require-persisted-operations/test-it
---

# 18.1 The shape

This is the [hub and distributed workers](/docs/reference/deployment-topologies) topology, which
chapter 11 set up on one machine:

```text
                 ┌──────────────┐
  storefront ───►│     hub      │──── schedules, serves GraphQL and the console, runs no job
  console    ───►│ (one)        │
                 └──────┬───────┘
                        │ jobs in Postgres          run events over RabbitMQ
              ┌─────────┴─────────┐ ◄──────────────────────────────────────┐
              ▼                   ▼                                        │
        ┌──────────┐        ┌──────────┐                                   │
        │ worker 1 │        │ worker 2 │ ── claim jobs, run them ───────────┘
        └──────────┘        └──────────┘
```

| Process | Registers | Must not |
|---|---|---|
| hub | `AddScheduler` with the Postgres submitter, `AddTraxGraphQL`, the console, the machines | run jobs itself |
| worker | `AddTraxWorker`, `AddMediator` over the same trains | `AddScheduler`, `AddTraxGraphQL` |

Workers claim jobs from Postgres, each job by exactly one worker, so a third worker is a third
container and nothing else.

## SDK Reference

> [Deployment topologies](/docs/reference/deployment-topologies) | [Move a domain data context to EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq) | [AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check) | [Health and liveness](/docs/scheduler/health-and-liveness) | [Startup refusals](/docs/security/startup-refusals) | [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard)

---

Next: [18.2 Migrate the shop's schema](/docs/book/deploy/migrate-the-shops-schema)
