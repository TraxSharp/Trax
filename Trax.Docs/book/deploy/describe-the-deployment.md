---
title: "19.6 Describe the deployment"
description: "Chapter 19 of the Trax book, section 6: describe Shopfront's deployment in trax.deploy.json, and what the hub and the worker report they need."
type: tutorial
area: general
parent: "19. Deploy"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/deploy/the-tests-migrate-too
redirect_from:
  - /docs/book/deploy/containers
---

# 19.6 Describe the deployment

`trax deploy` builds Shopfront's deployment from two sources. Each process reports what its code
needs: trax runs it once with `--trax-export-deploy`, in Production, and reads the report. A file
beside the solution, `trax.deploy.json`, holds the choices only you can make. It also builds the
images, with `dotnet publish`, so Shopfront needs no Dockerfile.

From the solution folder, with the `trax` tool installed in chapter 12:

```bash
dotnet trax deploy init
```

```text
Wrote /home/you/Shopfront/trax.deploy.json:
  hub         Shopfront.Hub/Shopfront.Hub.csproj
  worker      Shopfront.Worker/Shopfront.Worker.csproj
Name the settings and secrets the hosts read, then run 'trax deploy --target compose'.
```

`init` made a service of each host project and left out the test project and the library. Open the
file and make it this:

`trax.deploy.json`:

```json file=trax.deploy.json
{
  "version": 2,
  "app": "shopfront",
  "services": {
    "hub": {
      "project": "Shopfront.Hub/Shopfront.Hub.csproj",
      "memory": 512,
      "cpus": 1,
      "egress": ["login.example.com"]
    },
    "worker": {
      "project": "Shopfront.Worker/Shopfront.Worker.csproj",
      "memory": 384,
      "cpus": 0.5,
      "egress": []
    }
  },
  "connectionStrings": { "trax": "Shopfront", "rabbitMq": "RabbitMq" },
  "settings": [
    "Auth__Authority",
    "Auth__Audience",
    "Storefront__Origin",
    "Broadcast__AllowInsecureTransport"
  ],
  "secrets": ["Broadcast__SigningKey"],
  "environments": {
    "local": { "target": "compose", "drainTimeout": "00:05:00" },
    "cluster": {
      "target": "kubernetes",
      "profile": "try",
      "registry": "localhost:5001",
      "database": { "inCluster": true },
      "counts": { "worker": 2 },
      "drainTimeout": "00:05:00",
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

| In the file | What it does for Shopfront |
|---|---|
| `memory`, `cpus` | Each copy's limit: the hub 512 MiB and one core, a worker 384 MiB and half a core. A copy with no limit that leaks takes the memory Postgres needs. |
| `egress` | The domains each service may reach, beyond the deployment's own Postgres and RabbitMQ. The hub reaches the identity provider whose keys it checks tokens against; put yours in place of `login.example.com`, the host in `Auth__Authority`. The worker's fakes call nothing outside, so its list is empty: it reaches Postgres and RabbitMQ and nothing else, and code that starts calling out fails until its domain is listed. |
| `connectionStrings` | The names the hosts read: `ConnectionStrings:Shopfront` for Postgres, and the broker under `RabbitMq`. |
| `settings` | Configuration every environment must supply, as environment names (`__` for `:`): the identity provider, the storefront's origin, and whether the broker may be reached over plain AMQP. |
| `secrets` | The broadcast signing key. trax never writes a secret into a file it generates. |
| `local` | A compose stack on this machine, run by rootless podman: 19.7. |
| `cluster` | The same services on a Kubernetes cluster, with Postgres and RabbitMQ in it and two workers: 19.10. |
| `drainTimeout` | How long a release waits for the restock or the stock report to finish before it refuses. |

What is not in the file comes from the processes. Asked, the hub reports that it serves the API and
runs the scheduler, keeps its runs in Postgres, connects to RabbitMQ, and maps its GraphQL endpoint
and its health checks; the worker that it runs jobs, on the same Postgres and RabbitMQ. That is why
the hub runs one copy and is the only service with a port, why the stack has a broker, and why
nothing in the file says so. Change the code, and the next deploy asks again.

The export runs each host in Production with a stand-in for every connection string, setting and
secret, none of which reaches anything: the hub reads `Auth:Authority` as an `https://` address and
the signing key as base64 of 37 bytes, and gets past its startup checks without a database. This
is why 19.3 reads `Broadcast:AllowInsecureTransport` as the text `true`: given a URL, a boolean
parse would stop the hub before it reported.

A worker held to its egress runs one copy under compose, since its copies would share the egress
container's network and its ports; it runs two jobs at a time anyway (`WorkerCount = 2`). On a
cluster each copy has an egress container of its own, so `cluster` runs two.

`trax deploy` writes the stack beside the file, and you write a `.env` of secrets there. Keep them
out of git:

`.gitignore`:

```text file=.gitignore
# What trax deploy writes from trax.deploy.json, and the secrets beside it.
compose.yaml
.env.example
.env
.trax/
```

## SDK Reference

> [DeployManifest](/docs/sdk-reference/configuration/deploy-manifest) | [TraxMigrate](/docs/sdk-reference/configuration/trax-migrate) | [TraxDrain](/docs/sdk-reference/configuration/trax-drain) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq#reading-the-broker-from-configuration) | [Deploy a Trax app](/docs/reference/deploy-a-trax-app)

---

Next: [19.7 Run it as deployed](/docs/book/deploy/run-it-as-deployed)
