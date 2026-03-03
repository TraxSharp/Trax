---
layout: default
title: GraphQL API
parent: SDK Reference
nav_order: 8
has_children: true
---

# GraphQL API

Trax exposes a GraphQL endpoint powered by [HotChocolate](https://chillicream.com/docs/hotchocolate). It provides queries for inspecting trains, manifests, manifest groups, and executions, plus mutations for queuing/running trains and managing the scheduler.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTraxGraphQL();

var app = builder.Build();

app.UseTraxGraphQL(); // default: /graphql

app.Run();
```

Navigate to the endpoint URL in a browser to open [Banana Cake Pop](https://chillicream.com/docs/bananacakepop), the built-in GraphQL IDE. It provides schema exploration, autocompletion, and query execution without any extra tooling.

## Pages

| Page | Description |
|------|-------------|
| [AddTraxGraphQL]({{ site.baseurl }}{% link sdk-reference/graphql-api/add-trax-graphql.md %}) | Registration and endpoint mapping |
| [Queries]({{ site.baseurl }}{% link sdk-reference/graphql-api/queries.md %}) | `trains`, `manifests`, `manifest`, `manifestGroups`, `executions`, `execution` |
| [Mutations]({{ site.baseurl }}{% link sdk-reference/graphql-api/mutations.md %}) | `queueTrain`, `runTrain`, `triggerManifest`, `disableManifest`, `cancelManifest`, and more |

## Package

```
dotnet add package Trax.Api.GraphQL
```
