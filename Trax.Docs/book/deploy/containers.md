---
title: "18.6 Containers"
description: "Chapter 18 of the Trax book, section 6: Containers."
type: tutorial
area: general
parent: "18. Deploy"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/deploy/the-tests-migrate-too
---

# 18.6 Containers

One image per process, each built from the solution folder, since the hub and the worker both
reference the library. The SDK image builds and publishes; the runtime image runs, as a user that
is not root.

`Shopfront.Hub/Dockerfile`:

```dockerfile file=Shopfront.Hub/Dockerfile
# Built from the solution folder: docker build -f Shopfront.Hub/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The packages first, so a change to the code does not restore them again.
COPY Directory.Packages.props ./
COPY Shopfront.Core/Shopfront.Core.csproj Shopfront.Core/
COPY Shopfront.Hub/Shopfront.Hub.csproj Shopfront.Hub/
RUN dotnet restore Shopfront.Hub/Shopfront.Hub.csproj

COPY Shopfront.Core/ Shopfront.Core/
COPY Shopfront.Hub/ Shopfront.Hub/
# The machine's corpus, which the library embeds, and the storefront's documents, which the hub
# registers at start.
COPY machines/ machines/
COPY web/src/trax/trax.documents.json web/src/trax/
RUN dotnet publish Shopfront.Hub/Shopfront.Hub.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Shopfront.Hub.dll"]
```

`Shopfront.Worker/Dockerfile`:

```dockerfile file=Shopfront.Worker/Dockerfile
# Built from the solution folder: docker build -f Shopfront.Worker/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Packages.props ./
COPY Shopfront.Core/Shopfront.Core.csproj Shopfront.Core/
COPY Shopfront.Worker/Shopfront.Worker.csproj Shopfront.Worker/
RUN dotnet restore Shopfront.Worker/Shopfront.Worker.csproj

COPY Shopfront.Core/ Shopfront.Core/
COPY Shopfront.Worker/ Shopfront.Worker/
COPY machines/ machines/
RUN dotnet publish Shopfront.Worker/Shopfront.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Shopfront.Worker.dll"]
```

`.dockerignore`:

```text file=.dockerignore
**/bin/
**/obj/
**/node_modules/
web/dist/
```

The hub's image carries `trax.documents.json`, which it registers at start: the image is built
after `trax client`, from the same commit as the storefront.

## SDK Reference

> [Deployment topologies](/docs/reference/deployment-topologies) | [Move a domain data context to EF migrations](/docs/effect/move-a-domain-data-context-to-ef-migrations) | [UseRabbitMq](/docs/sdk-reference/configuration/use-rabbit-mq) | [AddTraxHealthCheck](/docs/sdk-reference/graphql-api/add-trax-health-check) | [Health and liveness](/docs/scheduler/health-and-liveness) | [Startup refusals](/docs/security/startup-refusals) | [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard)

---

Next: [18.7 Run it as deployed](/docs/book/deploy/run-it-as-deployed)
