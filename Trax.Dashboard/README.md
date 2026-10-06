# Trax.Dashboard

[![Build](https://github.com/TraxSharp/Trax/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/TraxSharp/Trax/actions/workflows/release.yml?query=branch%3Amain)
[![NuGet](https://img.shields.io/nuget/v/Trax.Dashboard)](https://www.nuget.org/packages/Trax.Dashboard)
[![codecov](https://codecov.io/gh/TraxSharp/Trax/branch/main/graph/badge.svg?flag=Trax.Dashboard)](https://app.codecov.io/gh/TraxSharp/Trax?flags%5B0%5D=Trax.Dashboard)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax/blob/main/Trax.Dashboard/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/dashboard)

> Part of [Trax .NET](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [Repository](https://github.com/TraxSharp/Trax)

Trax.Dashboard is a Blazor Server dashboard for Trax runs, schedules, dead letters and the work queue. It mounts at
`/trax` inside the ASP.NET Core app that already hosts Trax, so there is no separate service to deploy. It builds on
[Trax.Api](https://github.com/TraxSharp/Trax/tree/main/Trax.Api) and [Trax.Scheduler](https://github.com/TraxSharp/Trax/tree/main/Trax.Scheduler).

<!-- screenshot: dashboard run view -->

## Install

```bash
dotnet add package Trax.Dashboard
dotnet add package Trax.Effect.Data.Postgres   # storage the example below reads from
```

## Example

```csharp
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddMediator(typeof(Program).Assembly));

builder.Services.AddAuthorization(o =>
    o.AddPolicy("TraxAdmin", p => p.RequireRole("Admin")));

// Must follow AddTrax; it throws otherwise.
builder.AddTraxDashboard(dashboard => dashboard.RequirePolicy("TraxAdmin"));

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.UseTraxDashboard();   // mounts at /trax
```

The host's csproj also needs `<RequiresAspNetWebAssets>true</RequiresAspNetWebAssets>`; without it the dashboard renders
but ignores every click.

## Where this fits

Trax is split into layers, one folder of this repository each. Take the ones you need; the trains you wrote do not change. **You are here: Trax.Dashboard.**

| Folder | What it adds |
|---|---|
| [Trax.Core](https://github.com/TraxSharp/Trax/tree/main/Trax.Core) | Trains, junctions and the chain, with no database and no DI container |
| [Trax.Effect](https://github.com/TraxSharp/Trax/tree/main/Trax.Effect) | A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers, the state-machine engine |
| [Trax.Mediator](https://github.com/TraxSharp/Trax/tree/main/Trax.Mediator) | The train bus: run a train by handing over its input, with every chain checked at startup |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax/tree/main/Trax.Scheduler) | Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda |
| [Trax.Api](https://github.com/TraxSharp/Trax/tree/main/Trax.Api) | GraphQL generated from your trains, with authentication, audit and typed clients |
| **[Trax.Dashboard](https://github.com/TraxSharp/Trax/tree/main/Trax.Dashboard)** | **A Blazor Server UI for runs, schedules and dead letters, mounted in your app** |
| [Trax.Cli](https://github.com/TraxSharp/Trax/tree/main/Trax.Cli) | The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen |
| [Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples) | Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates |

Docs live in [Trax.Docs](https://github.com/TraxSharp/Trax/tree/main/Trax.Docs) and are published at [traxsharp.net/docs](https://traxsharp.net/docs).

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax/blob/main/Trax.Dashboard/AGENTS.md) before changing code. Report
vulnerabilities privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax/blob/main/Trax.Dashboard/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax .NET is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
