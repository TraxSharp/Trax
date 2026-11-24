---
title: "19.10 Before it takes real orders"
description: "Chapter 19 of the Trax book, section 10: Before it takes real orders."
type: tutorial
area: general
parent: "19. Deploy"
grand_parent: The Trax book
nav_order: 10
snippet_context: none
follows: book/deploy/the-console
---

# 19.10 Before it takes real orders

- **HTTPS** in front of the hub, terminated at the ingress or the load balancer.
- **Secrets** from a secret store, not a file in the repository.
- **Fakes.** The payment provider, the courier, the supplier and the warehouse are fakes, registered
  in `AddShopfront`. Each is an interface, so a real one replaces it there, and nothing else
  changes.
- **A second hub** needs persisted-operation changes broadcast between hubs: replace `SingleNode()`
  with `UseRabbitMqInvalidation(rabbitMq, o => o.SigningKey = broadcastKey)`. Only one hub runs the schedule at a time; the others
  serve.

- **One command in place of `compose.yaml`.** See below.

## With trax deploy

`compose.yaml` is a file you wrote and keep up to date by hand. `trax deploy` writes the deployment instead, from
what each process reports it needs when it is started with `--trax-export-deploy`, and adds what 19.7 leaves out.
As it applies to Shopfront:

- **Hardening.** Every container drops every capability and runs with a read-only root; on a cluster each pod also
  gets a user namespace of its own and no token for the cluster's API.
- **A drain before every release.** The restock runs hourly and the stock report nightly, on a worker. A release
  that replaced the hub while one was running would fail it: a hub that starts fails every run still in progress
  from before it started. A deploy drains first, waits for those runs to finish, and only then replaces the hub and
  the workers ([Drain before a deploy](/docs/reference/drain-before-a-deploy)).
- **No outage, and a rollback.** On a cluster the new hub starts beside the old one and takes the storefront's
  requests once it is ready; a release whose hub cannot start is put back.
- **What each process may reach.** The fakes reach nothing outside, so the worker needs nothing but Postgres and
  RabbitMQ, and the hub only the identity provider whose keys it checks tokens against. `"egress"` holds each to
  that.
- **Staff-only access.** 19.9 puts the console behind something that is the gate. `"exposure": "tailnet"` makes the
  hub reachable only from your tailnet's devices, which suits a deployment only staff use. Shopfront's storefront
  calls the hub from customers' browsers, and a public exposure is not offered yet, so for the shop itself the hub
  still needs an ingress or a load balancer in front of it.

Shopfront's `trax.deploy.json`, for a k3s cluster with Postgres and RabbitMQ in it, would be:

```json
{
  "version": 2,
  "app": "shopfront",
  "services": {
    "hub": { "project": "Shopfront.Hub/Shopfront.Hub.csproj", "memory": 512, "egress": ["login.example.com"] },
    "worker": { "project": "Shopfront.Worker/Shopfront.Worker.csproj", "count": 2, "memory": 512, "egress": [] }
  },
  "connectionStrings": { "trax": "Shopfront", "rabbitMq": "RabbitMq" },
  "settings": ["Auth__Authority", "Auth__Audience", "Storefront__Origin", "Broadcast__AllowInsecureTransport"],
  "secrets": ["Broadcast__SigningKey"],
  "environments": {
    "staging": {
      "target": "kubernetes",
      "profile": "try",
      "registry": "localhost:5001",
      "database": { "inCluster": true },
      "settings": {
        "Auth__Authority": "https://login.example.com/",
        "Auth__Audience": "shopfront",
        "Storefront__Origin": "https://shop.example.com",
        "Broadcast__AllowInsecureTransport": "true"
      }
    }
  }
}
```

It does not deploy Shopfront as this book leaves it. Four things in the code stand in the way, and this book has not
made those changes yet:

- **The worker ends with `app.RunAsync()`.** trax asks each process what it needs by starting it with
  `--trax-export-deploy`, which only `RunTraxAsync` answers; a process that calls `app.Run()` refuses to start.
- **The hub migrates the shop's schema before `RunTraxAsync`**, unless it is exporting its client. Asked for its
  deploy report, it would try to migrate a database that does not exist, since the report is made without one.
- **Both read the broker from `ConnectionStrings:RabbitMq`.** A deploy passes the broker's host, user name and
  password as `RabbitMq__Host`, `RabbitMq__UserName` and `RabbitMq__Password`, so no password sits inside a URI;
  `UseRabbitMq(builder.Configuration)` reads them.
- **Both decode `Broadcast:SigningKey` before they are built.** For the report, every secret is given a placeholder
  that is not base64, so both stop there.

Until then, [A zero-downtime deploy on k3s](/docs/reference/a-zero-downtime-deploy-on-k3s) walks the same path with a
`trax-hub` project, and [How a deploy works](/docs/reference/how-a-deploy-works) explains each step.

## What we have

Shopfront, deployed: a hub that serves the storefront and the console, workers that run the jobs,
and the two sharing one Postgres and one RabbitMQ. That is the end of the book.

## SDK Reference

> [DeployManifest](/docs/sdk-reference/configuration/deploy-manifest) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq#reading-the-broker-from-configuration) | [TraxDrain](/docs/sdk-reference/configuration/trax-drain)

---

That is the end of the book. [Back to the contents](/docs/book).
