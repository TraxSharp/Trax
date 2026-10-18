---
title: "11.6 The worker"
description: "Chapter 11 of the Trax book, section 6: The worker."
type: tutorial
area: general
parent: "11. A separate worker"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/a-separate-worker/the-hub
---

# 11.6 The worker

`Shopfront.Worker/Program.cs`:

```csharp compile=worker file=Shopfront.Worker/Program.cs
using Shopfront;
using Shopfront.Extensions;
using Shopfront.Staff;
using Trax.Api.Auth;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Shopfront")
    ?? throw new InvalidOperationException("ConnectionStrings:Shopfront is not set.");

builder.Services.AddShopfront(connectionString);
// The trains ask who the caller is. On the worker nobody is, and that is the answer they get.
builder.Services.AddTraxPrincipalAccessor();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .SaveTrainParameters()
                .AddJunctionLogger(serializeJunctionData: true)
                .AddDecisionRecording()
                .AddJunctionEvents()
                .AddLifecycleHook<StaffFailureHook>()
        )
        .AddMediator(mediator =>
            mediator
                .ScanAssemblies(typeof(ManifestNames).Assembly)
                // The hub authorized every job this worker runs before it was queued.
                .AllowMissingAuthorizationService()
        )
);
builder.Services.AddTraxWorker(worker => worker.WorkerCount = 2);

var app = builder.Build();
await app.RunAsync();
```

- **`AddShopfront`** is the same call the hub makes. When a process starts, Trax checks the chain of
  every train it registered, including the ones it never runs, so every process that registers the
  library's trains registers their services too.
- **`AddTraxPrincipalAccessor`** gives the trains a way to ask who the caller is. The hub gets it
  from its JWT sign-in; the worker has none, and the answer on the worker is nobody.
- **`AllowMissingAuthorizationService`** says this process enforces no `[TraxAuthorize]`. Without it
  the worker refuses to start, because the library holds `PlaceOrder`, which declares one, and
  nothing here would check it. That is safe only because nothing reaches the worker but the jobs
  the hub queued, after the hub checked who queued them.
- **`AddTraxWorker`** claims queued jobs from Postgres and runs them, two at a time. That is the
  cap: at most two fulfilments, or restocks, or any mix, run at once on this worker.

The worker reads the same configuration as the hub.

`Shopfront.Worker/appsettings.json`:

```json file=Shopfront.Worker/appsettings.json
{
  "ConnectionStrings": {
    "Shopfront": "Host=localhost;Database=shopfront;Username=postgres;Password=shopfront"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

## SDK Reference

> [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Queued work](/docs/mediator/queued-work) | [Job submission](/docs/scheduler/job-submission) | [Structuring your application](/docs/reference/project-layout) | [Host tracking](/docs/effect/host-tracking)

---

Next: [11.7 Run both](/docs/book/a-separate-worker/run-both)
