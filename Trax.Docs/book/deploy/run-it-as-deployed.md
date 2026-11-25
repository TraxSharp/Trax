---
title: "19.7 Run it as deployed"
description: "Chapter 19 of the Trax book, section 7: deploy Shopfront on this machine with trax deploy and podman compose, then release a new version over it."
type: tutorial
area: general
parent: "19. Deploy"
grand_parent: The Trax book
nav_order: 7
snippet_context: none
follows: book/deploy/describe-the-deployment
---

# 19.7 Run it as deployed

The `local` environment is a compose stack on this machine, run by rootless podman: Postgres,
RabbitMQ, the hub and a worker. You need podman with `podman compose`. podman's runtime must be
crun, the default on most distributions: runc cannot start a container in another container's
network namespace when each has a user namespace of its own, which is how the egress is held.

Write the stack and build the images:

```bash
dotnet trax deploy --env local --build
```

```text
Asking hub what it needs (Shopfront.Hub/Shopfront.Hub.csproj)...
Asking worker what it needs (Shopfront.Worker/Shopfront.Worker.csproj)...
Wrote /home/you/Shopfront/compose.yaml
Wrote /home/you/Shopfront/.env.example
Building shopfront-hub...
Building shopfront-worker...
Copy .env.example to .env, fill it in, then in /home/you/Shopfront run:
  podman compose up -d migrate drain && podman compose wait drain && podman compose up -d
```

## What trax wrote

`compose.yaml` is trax's, rewritten by every `trax deploy`; you read it, not edit it. Its first
block is what every Shopfront process is given:

```yaml
x-trax: &trax
  ConnectionStrings__Shopfront: Host=database;Database=shopfront;Username=shopfront;GSS Encryption Mode=Disable
  PGPASSWORD: ${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD}
  RabbitMq__Host: rabbitmq
  RabbitMq__UserName: shopfront
  RabbitMq__Password: ${RABBITMQ_PASSWORD:?set RABBITMQ_PASSWORD}
  Auth__Audience: ${Auth__Audience:?set Auth__Audience}
  Auth__Authority: ${Auth__Authority:?set Auth__Authority}
  Broadcast__AllowInsecureTransport: ${Broadcast__AllowInsecureTransport:?set Broadcast__AllowInsecureTransport}
  Broadcast__SigningKey: ${Broadcast__SigningKey:?set Broadcast__SigningKey}
  Storefront__Origin: ${Storefront__Origin:?set Storefront__Origin}
```

No password is inside a connection string or a URI: Npgsql reads `PGPASSWORD`, and the hub and the
worker read the broker's host, user and password apart, as 19.3 set them up to. Every value comes
from `.env`, and compose stops when one is missing. The hub, trimmed:

```yaml
  hub:
    image: shopfront-hub:${TRAX_IMAGE_TAG:-latest}
    environment:
      <<: *trax
      TRAX_MIGRATIONS: skip
    network_mode: "service:hub-egress"
    cap_drop:
      - ALL
    security_opt:
      - no-new-privileges:true
    userns_mode: nomap
    read_only: true
    tmpfs:
      - /tmp
    pids_limit: 512
    cpus: 1
    mem_limit: 512m
    memswap_limit: 512m
    depends_on:
      migrate:
        condition: service_completed_successfully
      hub-egress:
        condition: service_healthy
      drain:
        condition: service_completed_successfully
```

What each part does for Shopfront:

- **Hardened.** Every Linux capability dropped, a read-only root with a scratch `/tmp`, a user
  namespace that leaves your own uid out of it (`nomap`), at most 512 processes, and the memory and
  CPU from `trax.deploy.json` with no swap on top. Postgres and RabbitMQ get the same, less the
  read-only root and plus the few capabilities their entrypoints need.
- **Held to its egress.** The hub joins the network namespace of `hub-egress`, which runs the egress
  image: it reaches `login.example.com` on 443, `database` and `rabbitmq`, and nothing else. The
  hub's port is published there, on `127.0.0.1:8080` only.
- **Migrated once, before it starts.** `migrate` runs the new hub image with `--trax-migrate`,
  which applies Trax's migrations and the shop's (`MigrateOnDeploy`) and exits. Every serving
  process has `TRAX_MIGRATIONS=skip`.
- **Drained before it is replaced.** `drain` runs the same image with `--trax-drain 00:05:00`: every
  running host stops starting new work, and it waits for what is busy. `resume` ends the drain
  once the services have started.

## Start it

`.env` holds the passwords, the settings and the key. Copy the example and fill it in, or write it
in one go:

```bash
cat > .env <<EOF
POSTGRES_PASSWORD=$(openssl rand -base64 24)
RABBITMQ_PASSWORD=$(openssl rand -base64 24)
Auth__Authority=https://login.example.com/
Auth__Audience=shopfront
Storefront__Origin=https://shop.example.com
Broadcast__AllowInsecureTransport=true
Broadcast__SigningKey=$(openssl rand -base64 32)
TRAX_DRAIN_FORCE=false
TRAX_IMAGE_TAG=latest
EOF
chmod 600 .env
```

`Auth__Authority` is your identity provider, and its host is the hub's `egress`.
`Broadcast__AllowInsecureTransport` is `true` because the broker trax runs is reached by name on the
stack's own network. A base64 password often holds a `/` or a `+`, which reaches Postgres and
RabbitMQ intact. Then run what trax printed:

```bash
podman compose up -d migrate drain && podman compose wait drain && podman compose up -d
```

The first command migrates and starts the drain, the second waits for the drain to finish, and
the third starts the hub and the worker, running the migration and the drain once more, which find
nothing to do. `migrate` applied the shop's first migration:

```bash
podman compose logs migrate
```

```text
info: Microsoft.EntityFrameworkCore.Migrations[20402]
      Applying migration '20261020023340_Initial'.
info: Trax.Effect.Data.Extensions.TraxDeployExtensions[0]
      ShopfrontDbContext: applied 20261020023340_Initial.
info: Trax.Effect.Data.Extensions.TraxDeployExtensions[0]
      Migration finished; this process does not serve.
```

EF Core logs two `fail:` lines before that, the first time: it looks for the history table before it
creates it. On a fresh database that is expected. The drain found nothing to wait for:

```text
info: Trax.Effect.Data.Services.Drain.DrainCommand[0]
      Drained: no host starts new work until 2026-11-25 16:44:44Z. Waiting up to 00:05:00 for what is busy.
info: Trax.Effect.Data.Services.Drain.DrainCommand[0]
      Nothing is busy; the hosts can be replaced.
```

The hub answers on `localhost:8080`:

```bash
curl -s http://localhost:8080/health/live
curl -s http://localhost:8080/health/ready
CAT=$(grep -o 'trax_client_Catalogue_[a-z0-9]*' web/src/trax/documents/catalogue.g.ts)
curl -s http://localhost:8080/trax/graphql -H 'Content-Type: application/json' -d '{"id":"'$CAT'"}'
```

```text
Healthy
Healthy
{"data":{"discover":{"products":{"nodes":[{"sku":"CAP","name":"Cap","price":18.50,"stock":5},{"sku":"MUG","name":"Mug","price":12.00,"stock":10},{"sku":"TEE","name":"T-shirt","price":25.00,"stock":8}]}}}}
```

The catalogue has more caps and T-shirts than it started with: the restock ran as soon as the hub
scheduled it, on the worker, whose log (`podman compose logs worker`) says `Received 2 deliveries`.

A published build runs in Production unless its environment says otherwise, and nothing here says
otherwise. So:

- **The broadcast key comes from `.env`.** The Development key is not read, and a process without
  the key does not start.
- **The hub accepts only tokens from your identity provider**, `Auth:Authority` and
  `Auth:Audience`. The Development key is refused outside Development, and `/dev/token` answers
  `404`.
- **Only registered documents run**, and `PersistedOperations:Require` is ignored. A document sent
  as text gets `PERSISTED_OPERATION_REQUIRED`, as in chapter 18.
- **The GraphQL IDE and schema download are not served.**
- **Development's short timeouts and fast retries are off**: a run whose worker died is failed
  after more than an hour, not a minute, and the restock runs hourly.

Each process also warns that it reaches RabbitMQ without TLS, which is what
`Broadcast__AllowInsecureTransport` allows for the broker on the stack's network. The hub warns that
ASP.NET Core keeps its data-protection keys in memory, since its root is read-only. Shopfront signs
nothing with them: its callers carry bearer tokens and no cookies. A hub that adds cookie sign-in
keeps those keys somewhere that outlives the container.

The egress image checks what a service can reach from inside its namespace. For the worker:

```bash
podman run --rm --network container:shopfront-worker-egress-1 --userns nomap --user 1654:1654 --cap-drop ALL \
  -e TRAX_EGRESS_ALLOW="tcp:database:5432 tcp:rabbitmq:5672" \
  "$(podman inspect -f '{{.ImageName}}' shopfront-worker-egress-1)" check
```

```text
ok   control 192.0.2.1:9                              intercepted    (want intercepted)
ok   tcp     database:5432                            open           (want open)
ok   tcp     rabbitmq:5672                            open           (want open)
ok   dns     example.com                              blocked        (want blocked)
ok   dns     example.com @1.1.1.1                     blocked        (want blocked)
ok   tls     example.com via 1.1.1.1:443              blocked        (want blocked)
ok   tcp     169.254.169.254:80                       blocked        (want blocked)
...
ok   nft     flush ruleset                            blocked        (want blocked)
egress as intended
```

## A new version

Change something, and release it the same way:

```bash
dotnet trax deploy --env local --build
podman compose up -d migrate drain && podman compose wait drain && podman compose up -d
```

The old hub and worker keep running while the new version migrates and drains. The restock runs
hourly and the stock report nightly, on the worker; a release that replaced the worker in the
middle of one would cut it off, and the new hub, starting, would fail every run still in progress
from before it started. So the drain stops every host starting new work and waits. The fakes take
no time, so you will rarely catch a restock running; when one is, the drain's log says so every two
seconds, and the old containers serve until it ends:

```text
info: Trax.Effect.Data.Services.Drain.DrainCommand[0]
      Drained: no host starts new work until 2026-11-25 16:37:30Z. Waiting up to 00:05:00 for what is busy.
info: Trax.Effect.Data.Services.Drain.DrainCommand[0]
      1 still busy, 00:04:54.2085802 left before the timeout.
```

When the restock ends, the drain exits, the hub and the worker are replaced, and the new hub ends
the drain as it starts. Work still busy after `drainTimeout` refuses the release: the drain fails,
naming it, `compose wait` returns its exit code, and nothing is replaced. `TRAX_DRAIN_FORCE=true` on the
release replaces the services anyway.

Run the three commands, not a bare `podman compose up -d`: compose stops every container whose image
changed before it starts any, so the old worker would be gone before the drain began. Under compose
the hub is down while its container is replaced, for a few seconds; on a cluster it is not (19.10).

To stop it, `podman compose down`; the database's volume stays, and `down -v` removes it too.

## SDK Reference

> [DeployManifest](/docs/sdk-reference/configuration/deploy-manifest) | [TraxMigrate](/docs/sdk-reference/configuration/trax-migrate) | [TraxDrain](/docs/sdk-reference/configuration/trax-drain) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq#reading-the-broker-from-configuration) | [Deploy a Trax app](/docs/reference/deploy-a-trax-app) | [Drain before a deploy](/docs/reference/drain-before-a-deploy)

---

Next: [19.8 The storefront](/docs/book/deploy/the-storefront)
