# Trax.Samples.Templates

`dotnet new` templates for [Trax .NET](https://github.com/TraxSharp): business logic you can call, schedule, or serve as
an API, with every run recorded.

```bash
dotnet new install Trax.Samples.Templates
dotnet new trax-hub -n MyApp      # or trax-api, trax-scheduler
cd MyApp && dotnet run            # http://localhost:5400/trax/graphql and /trax
dotnet test tests/MyApp.Tests
```

| Template | What you get |
|---|---|
| `trax-api` | A GraphQL API generated from your trains |
| `trax-scheduler` | A scheduler that runs trains on cron and interval manifests, with the dashboard |
| `trax-hub` | Both in one process: the API, the scheduler and the dashboard |

Each template stores runs in memory, so it needs no database, and ships a README and a test project. The dashboard
and the demo API key exist only in Development.

Docs: <https://traxsharp.net/docs/getting-started> · Source: <https://github.com/TraxSharp/Trax.Samples>
