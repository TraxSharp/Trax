# Trax.Samples

[![Build](https://github.com/TraxSharp/Trax.Samples/actions/workflows/nuget_release.yml/badge.svg)](https://github.com/TraxSharp/Trax.Samples/actions/workflows/nuget_release.yml)
[![NuGet Version](https://img.shields.io/nuget/v/Trax.Samples.Templates)](https://www.nuget.org/packages/Trax.Samples.Templates/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Trax.Samples.Templates)](https://www.nuget.org/packages/Trax.Samples.Templates/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Last Commit](https://img.shields.io/github/last-commit/TraxSharp/Trax.Samples)](https://github.com/TraxSharp/Trax.Samples/commits/main)
[![codecov](https://codecov.io/gh/TraxSharp/Trax.Samples/branch/main/graph/badge.svg)](https://codecov.io/gh/TraxSharp/Trax.Samples)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs)

Sample applications and a `dotnet new` project template for getting started with [Trax](https://www.nuget.org/packages/Trax.Core/).

## The Trax Stack

Trax is a layered framework split across several repos. You can stop at whatever layer solves your problem. **You are here: Trax.Samples.**

| Repo | Adds |
|------|------|
| [Trax.Core](https://github.com/TraxSharp/Trax.Core) | Pipelines, junctions, railway error propagation |
| [Trax.Effect](https://github.com/TraxSharp/Trax.Effect) | Execution logging, DI, pluggable storage |
| [Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator) | Decoupled dispatch via `TrainBus` |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax.Scheduler) | Cron schedules, retries, dead-letter queues |
| [Trax.Api](https://github.com/TraxSharp/Trax.Api) | GraphQL API for remote access |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard) | Blazor monitoring UI |
| [Trax.Cli](https://github.com/TraxSharp/Trax.Cli) | `trax-cli` project scaffolding tool |
| **[Trax.Samples](https://github.com/TraxSharp/Trax.Samples)** | Sample apps and a `dotnet new` template |

Full documentation: [traxsharp.net/docs](https://traxsharp.net/docs).

## Project Template

Scaffold a new Trax server with the control room, timetable, and PostgreSQL persistence already wired up:

```bash
dotnet new install Trax.Samples.Templates
dotnet new trax-server -n MyApp
```

With a custom connection string:

```bash
dotnet new trax-server -n MyApp --ConnectionString "Host=db.example.com;Port=5432;Database=myapp;Username=myuser;Password=secret"
```

The template creates an ASP.NET Core project with:

- `AddTrax` configured with `AddEffects` (Postgres, junction logging, junction progress), `AddMediator`, and `AddScheduler`
- `AddTraxDashboard` for the control room
- `AddScheduler` with a sample `HelloWorldTrain` departing every 20 seconds
- A `Trains/` directory with an example train, cargo type, interface, and stop

## Sample Applications

### Scheduler Sample

A full working example of the Trax scheduler with ETL-style pipelines, fleet scheduling, connected departures, dormant trains, and journey log cleanup.

**Running it:**

```bash
# Start PostgreSQL
cd Trax.Samples
docker compose up -d

# Run the sample
dotnet run --project samples/Trax.Samples.Scheduler
```

Then open `http://localhost:5298/trax` to see the control room.

The sample includes:

- **HelloWorldTrain** — a simple scheduled train that logs a greeting every 20 seconds
- **ExtractImportTrain** — a multi-stop ETL train with 10 parallel manifests departing every 5 minutes
- **TransformLoadTrain** — a connected departure that runs after extract arrives
- **DataQualityCheckTrain** — a dormant train waiting in the yard, activated from a junction when anomalies are detected
- Journey log cleanup configuration for the HelloWorld train

### Flowthru Spaceflights

A data pipeline sample using the [Flowthru](https://github.com/chaoticgoodcomputing/flowthru) project conventions, demonstrating Trax trains in an ML-style pipeline context.

## License

MIT

## Trademark & Brand Notice

Trax is an open-source .NET framework provided by TraxSharp. This project is an independent community effort and is not affiliated with, sponsored by, or endorsed by the Utah Transit Authority, Trax Retail, or any other entity using the "Trax" name in other industries.
