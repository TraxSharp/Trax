---
title: "18.8 The storefront"
description: "Chapter 18 of the Trax book, section 8: The storefront."
type: tutorial
area: general
parent: "18. Deploy"
grand_parent: The Trax book
nav_order: 8
snippet_context: none
follows: book/deploy/run-it-as-deployed
---

# 18.8 The storefront

The storefront is static files. Build it with the hub's address, and serve `web/dist` from
wherever the shop's pages are served, at the origin `Storefront:Origin` names:

```bash
cd web
VITE_HUB_URL=https://api.shop.example.com npm run build
```

`web/src/connection.ts`:

```ts file=web/src/connection.ts
import { configureTrax } from "./trax/index.g";

// Where the hub is: from the build's environment, or the hub `dotnet run` starts.
const HUB = import.meta.env.VITE_HUB_URL ?? "http://localhost:5080";

// Signs in as a customer with a Development token from the hub, and points the generated client at
// it. Every request carries the token; the socket, which cannot carry a header, sends it when it
// opens. A deployed storefront gets its token from the identity provider the hub trusts instead.
export async function signIn(user: string): Promise<void> {
  const token = await (await fetch(`${HUB}/dev/token/${user}`)).text();
  configureTrax({
    url: `${HUB}/trax/graphql`,
    socketUrl: `${HUB.replace("http", "ws")}/trax/graphql`,
    headers: () => ({ Authorization: `Bearer ${token}` }),
    connectionParams: () => ({ authToken: token }),
    user,
  });
}
```

`web/src/vite-env.d.ts`:

```ts file=web/src/vite-env.d.ts
/// <reference types="vite/client" />
```

A deployed storefront signs customers in with the identity provider the hub trusts, and hands the
token it gets to `configureTrax`, as the page does with the Development token now.

## SDK Reference

> [Deployment topologies](/docs/reference/deployment-topologies) | [Move a domain data context to EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq) | [AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check) | [Health and liveness](/docs/scheduler/health-and-liveness) | [Startup refusals](/docs/security/startup-refusals) | [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard)

---

Next: [18.9 The console](/docs/book/deploy/the-console)
