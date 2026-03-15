# Trax.Samples

[![NuGet Version](https://img.shields.io/nuget/v/Trax.Samples.Templates)](https://www.nuget.org/packages/Trax.Samples.Templates/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Sample applications and a `dotnet new` project template for getting started with [Trax](https://www.nuget.org/packages/Trax.Core/).

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

- `AddTrax` configured with `AddEffects` (Postgres, step logging, step progress), `AddMediator`, and `AddScheduler`
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
- **DataQualityCheckTrain** — a dormant train waiting in the yard, activated from a step when anomalies are detected
- Journey log cleanup configuration for the HelloWorld train

### Flowthru Spaceflights

A data pipeline sample using the [Flowthru](https://github.com/chaoticgoodcomputing/flowthru) project conventions, demonstrating Trax trains in an ML-style pipeline context.

## Related Packages

| Package | Purpose |
|---------|---------|
| [Trax.Core](https://www.nuget.org/packages/Trax.Core/) | The locomotive — `Train`, steps, railway programming |
| [Trax.Effect](https://www.nuget.org/packages/Trax.Effect/) | `ServiceTrain` with journey logging and station services |
| [Trax.Mediator](https://www.nuget.org/packages/Trax.Mediator/) | Dispatch station — route cargo to the right train via `TrainBus` |
| [Trax.Scheduler](https://www.nuget.org/packages/Trax.Scheduler/) | Timetables — recurring trains with retries and dead-lettering |
| [Trax.Dashboard](https://www.nuget.org/packages/Trax.Dashboard/) | Control room — monitor every journey on the network |

Full documentation: [traxsharp.net/docs](https://traxsharp.net/docs)

## License

MIT

## Trademark & Brand Notice

Trax is an open-source .NET framework provided by TraxSharp. This project is an independent community effort and is not affiliated with, sponsored by, or endorsed by the Utah Transit Authority, Trax Retail, or any other entity using the "Trax" name in other industries.
