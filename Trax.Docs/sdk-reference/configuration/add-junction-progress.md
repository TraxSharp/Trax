---
title: AddJunctionProgress
description: Reference for AddJunctionProgress, the junction effects that record the running junction and stop a run whose cancellation was requested, before each junction.
type: reference
area: effects
parent: Configuration
grand_parent: SDK Reference
nav_order: 23
package: Trax.Effect.JunctionProvider.Progress
namespace:
  - Trax.Effect.JunctionProvider.Progress.Extensions
  - Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckProvider
source:
  - Trax.Effect/src/Trax.Effect.JunctionProvider.Progress/Extensions/ServiceExtensions.cs
  - Trax.Effect/src/Trax.Effect.JunctionProvider.Progress/Services/CancellationCheckProvider/CancellationCheckProvider.cs
---

# AddJunctionProgress

Adds two junction effects: a cancellation check that stops a run whose cancellation was requested from another
process, and a progress writer that records which junction the run is on.

## Signature

```csharp fragment
public static TBuilder AddJunctionProgress<TBuilder>(
    this TBuilder configurationBuilder
)
    where TBuilder : TraxEffectBuilder
```

## Returns

`TBuilder`, the builder type it was called on.

## Behavior

Sets the builder's `JunctionProgressEnabled` and registers both providers with `AddJunctionEffect`, as toggleable
junction effects that show on the dashboard's Effects page, the cancellation check first. Without a data provider,
`AddEffects` throws `InvalidOperationException` naming `UsePostgres()`, `UseSqlite()` and `UseInMemory()`.

| Provider | Before each `EffectJunction` | After it |
|----------|------------------------------|----------|
| `CancellationCheckProvider` | Reads the run's `Metadata.CancellationRequested` from the database through a context of its own. When it is set, sets it on the run and throws `CancellationRequestedException` (an `OperationCanceledException`), and the run is recorded as `Cancelled`. | Nothing |
| `JunctionProgressProvider` | Writes `Metadata.CurrentlyRunningJunction` and `Metadata.JunctionStartedAt` | Clears them. The clearing write runs on `CancellationToken.None` and logs a failure instead of throwing, so it never replaces the result of a junction whose work already returned. |

The progress writer writes those two columns, and only those, through a data context of its own. It does not save
the run's other effects: everything else the run tracks is committed when the run's outcome is written.

## Example

```csharp compile
services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .AddJunctionProgress()
    )
    .AddMediator(typeof(IShipOrderTrain).Assembly)
);
```

## See also

- [Junction Progress](/docs/effect/effect-providers/junction-progress)
- [AddJunctionEffect](/docs/sdk-reference/configuration/add-effect)
