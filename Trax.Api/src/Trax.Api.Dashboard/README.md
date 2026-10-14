# Trax.Api.Dashboard

[![NuGet](https://img.shields.io/nuget/v/Trax.Api.Dashboard)](https://www.nuget.org/packages/Trax.Api.Dashboard)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax/blob/main/Trax.Api/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/sdk-reference/api-dashboard)

> Part of [Trax .NET](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [Repository](https://github.com/TraxSharp/Trax)

Trax.Api.Dashboard is the Trax operations dashboard as a single-page app your Trax.Api host serves: runs, the work queue,
dead letters, manifests, logs, trains, effects and state machines. The built app is inside the package, so the host
needs no Node, and everything the app shows or changes goes through the host's Trax GraphQL endpoint. You can try it on
recorded data at [traxsharp.net/dashboard](https://traxsharp.net/dashboard).

## Install

```bash
dotnet add package Trax.Api.Dashboard
```

## Example

```csharp
builder.Services.AddTraxGraphQL(graphql => graphql.RequireAuthorization());
builder.Services.AddAuthorization(o =>
    o.AddPolicy("TraxAdmin", p => p.RequireRole("Admin")));

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxGraphQL();                                                   // /trax/graphql
app.UseTraxApiDashboard(dashboard => dashboard.RequirePolicy("TraxAdmin")); // /trax/dashboard
```

The dashboard refuses to start until you say who may load it: `RequirePolicy`, `RequireRoles`, or
`AllowAnonymousShell()` when something in front of it is the gate. The app then asks the operator for an API key or
bearer token and sends it to the GraphQL endpoint, which authorizes every read and write on its own.

## Where this fits

Trax is split into layers, one folder of this repository each. Take the ones you need; the trains you wrote do not change. **You are here: Trax.Api.**

| Folder | What it adds |
|---|---|
| [Trax.Core](https://github.com/TraxSharp/Trax/tree/main/Trax.Core) | Trains, junctions and the chain, with no database and no DI container |
| [Trax.Effect](https://github.com/TraxSharp/Trax/tree/main/Trax.Effect) | A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers, the state-machine engine |
| [Trax.Mediator](https://github.com/TraxSharp/Trax/tree/main/Trax.Mediator) | The train bus: run a train by handing over its input, with every chain checked at startup |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax/tree/main/Trax.Scheduler) | Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda |
| **[Trax.Api](https://github.com/TraxSharp/Trax/tree/main/Trax.Api)** | **GraphQL generated from your trains, with authentication, audit, typed clients, and this dashboard** |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax/tree/main/Trax.Dashboard) | A Blazor Server UI for runs, schedules and dead letters, mounted in your app |
| [Trax.Cli](https://github.com/TraxSharp/Trax/tree/main/Trax.Cli) | The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen |
| [Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples) | Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates |

Docs live in [Trax.Docs](https://github.com/TraxSharp/Trax/tree/main/Trax.Docs) and are published at [traxsharp.net/docs](https://traxsharp.net/docs).

## Contributing

The app's source is in [`ClientApp/`](https://github.com/TraxSharp/Trax/tree/main/Trax.Api/src/Trax.Api.Dashboard/ClientApp),
its own npm project. Read [AGENTS.md](https://github.com/TraxSharp/Trax/blob/main/Trax.Api/AGENTS.md) before changing
code. Report vulnerabilities privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax/blob/main/Trax.Api/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax .NET is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
