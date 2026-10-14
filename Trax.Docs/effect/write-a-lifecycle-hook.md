---
title: Write a lifecycle hook
description: How to run code when trains start, complete, fail or are cancelled, with a hook registered for every train or an override on one train.
type: how-to
area: effects
nav_order: 20
---

# Write a lifecycle hook

A lifecycle hook reacts to a run's outcome (post to a chat channel, emit a metric, clean something up) without the
train knowing about it. Pick where it lives first:

| | A registered hook | An override on one train |
|---|---|---|
| Runs for | Every train | That train only |
| Registered with | `AddLifecycleHook<T>()` | Nothing: override the method |
| Dependencies | The hook's constructor | The train's constructor |
| Order | First | After the registered hooks |
| Fits | Concerns every train shares: metrics, audit, alerts | One train's own follow-up |

Either way, an exception from the hook is logged and never fails the run.

## A hook for every train

1. Implement `ITrainLifecycleHook`. Every method has a default, so override only the events you need:

   ```csharp compile=hook
   using Trax.Effect.Models.Metadata;
   using Trax.Effect.Services.TrainLifecycleHook;

   public interface ISlackClient
   {
       Task PostAsync(string text, CancellationToken ct);
   }

   public sealed class SlackNotificationHook(ISlackClient slack) : ITrainLifecycleHook
   {
       public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
           slack.PostAsync($"Train {metadata.Name} failed: {exception.Message}", ct);
   }
   ```

   The hook is built for each run from the run's scope, so a scoped dependency is that run's.

2. Register it inside `AddEffects`:

   ```csharp compile=hook
   using Trax.Effect.Data.Postgres.Extensions;
   using Trax.Effect.Extensions;

   var builder = WebApplication.CreateBuilder(args);

   builder.Services.AddTrax(trax => trax
       .AddEffects(effects => effects
           .AddLifecycleHook<SlackNotificationHook>()
           .UsePostgres(builder.Configuration.GetConnectionString("Trax")!)
       )
   );

   builder.Build().Run();
   ```

   `AddLifecycleHook` returns `TraxEffectBuilder`, so put it before the data provider when a later call in the
   chain needs `TraxEffectBuilderWithData`.

3. To read the run's input or output, use `metadata.GetInput<T>()` and, in `OnCompleted`, `metadata.GetOutput<T>()`.
   Each returns `default` when the run is of another train, so a shared hook can check for the type it cares about.

## A hook on one train

Override the method on the train. `TrainInput` and `TrainOutput` are typed by the train's own generic arguments:

```csharp compile=override
using Trax.Core.Functional;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

public record BanPlayerInput(string PlayerId, string Reason);

public interface IBanPlayerTrain : IServiceTrain<BanPlayerInput, Unit>;

public class ApplyBanJunction : EffectJunction<BanPlayerInput, Unit>
{
    public override Task<Unit> Run(BanPlayerInput input) => Task.FromResult(Unit.Default);
}

public class BanPlayerTrain(ILogger<BanPlayerTrain> logger)
    : ServiceTrain<BanPlayerInput, Unit>, IBanPlayerTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<ApplyBanJunction>().Resolve();

    protected override Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        logger.LogInformation("Banned {PlayerId}: {Reason}", TrainInput.PlayerId, TrainInput.Reason);
        return Task.CompletedTask;
    }

    protected override Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
    {
        logger.LogWarning("Could not ban {PlayerId}: {Message}", TrainInput.PlayerId, exception.Message);
        return Task.CompletedTask;
    }
}
```

`OnStarted`, `OnCompleted`, `OnFailed` and `OnCancelled` can be overridden; there is no per-train `OnStateChanged`.

## See also

- [Metadata](/docs/effect/metadata), what a hook's `metadata` carries

## SDK Reference

> [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [UsePostgres](/docs/sdk-reference/configuration/use-postgres)
