# Trax SignalR Dashboard sample

A single-process Blazor Server app that exercises the
`Trax.Effect.Broadcaster.SignalR` sink end-to-end.

The scheduler queues a `PingTrain` every 10 seconds and an in-process worker
executes it. Every lifecycle event flows through the SignalR sink to connected
browsers via the `/hubs/trax-events` hub.

## What's wired up

- `services.AddSignalR()` (required by both Blazor Server and the Trax sink)
- `services.AddRazorComponents().AddInteractiveServerComponents()`
- `UseBroadcaster(b => b.UseSignalRHub(opts => opts.OnlyForEvents(...)))`
- `app.MapTraxTrainEventHub()` (default path `/hubs/trax-events`)
- Scheduler with one manifest (`PingTrain`, every 10s)
- `LocalWorkerService` (auto-registered by the scheduler with Postgres)

The `Events.razor` page opens its own `HubConnection` to `/hubs/trax-events`,
subscribes to the `TrainEvent` callback, and renders a rolling list of the
last 50 events.

## Run

```sh
# 1. Start Postgres (the workspace docker-compose ships one already)
cd ../.. && docker compose up -d

# 2. Pack local packages so Trax.Effect.Broadcaster.SignalR is resolvable
cd ../.. && ./pack-local.sh

# 3. Run the sample
dotnet run --project samples/SignalRDashboard/Trax.Samples.SignalRDashboard
```

Then open:

- <http://localhost:5210/events> — live event feed
- <http://localhost:5210/trax> — Trax dashboard
- <http://localhost:5210/hubs/trax-events> — SignalR hub endpoint (connect a client here)

## Verifying with a standalone client

If you want to confirm events without opening a browser, a 30-line console
client using `Microsoft.AspNetCore.SignalR.Client` is enough:

```csharp
var connection = new HubConnectionBuilder()
    .WithUrl("http://localhost:5210/hubs/trax-events")
    .Build();
connection.On<JsonElement>("TrainEvent", evt => Console.WriteLine(evt));
await connection.StartAsync();
await Task.Delay(TimeSpan.FromMinutes(1));
```

Expected output includes events for the scheduler's own trains
(`ManifestManagerTrain`, `JobDispatcherTrain`, `JobRunnerTrain`) and the
sample's `IPingTrain` every 10 seconds.
