---
title: "9.3 The low-stock check"
description: "Chapter 9 of the Trax book, section 3: The low-stock check."
type: tutorial
area: general
parent: "9. Scheduled work"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/scheduled-work/the-stock-report
---

# 9.3 The low-stock check

Staff hear about low stock through whatever the shop uses to reach them. Shopfront logs it for now,
behind an interface, so a real channel replaces one class later.

`Staff/IStaffNotifier.cs`:

```csharp compile=app file=Staff/IStaffNotifier.cs
namespace Shopfront.Staff;

public interface IStaffNotifier
{
    Task Notify(string message, CancellationToken cancellationToken);
}
```

`Staff/LoggingStaffNotifier.cs`:

```csharp compile=app file=Staff/LoggingStaffNotifier.cs
namespace Shopfront.Staff;

// Writes each message to the log. A real shop sends it to a channel staff read.
public class LoggingStaffNotifier(ILogger<LoggingStaffNotifier> logger) : IStaffNotifier
{
    public Task Notify(string message, CancellationToken cancellationToken)
    {
        logger.LogWarning("For staff: {Message}", message);
        return Task.CompletedTask;
    }
}
```

The check's input carries the one setting it has: how few is too few. Being stored with the
manifest, it can be changed without a release.

`Trains/LowStockCheck/LowStockCheckInput.cs`:

```csharp compile=app file=Trains/LowStockCheck/LowStockCheckInput.cs
using Trax.Effect.Models.Manifest;

namespace Shopfront.Trains.LowStockCheck;

public record LowStockCheckInput : IManifestProperties
{
    public int Threshold { get; init; } = 3;
}
```

`Trains/LowStockCheck/LowStockCheckOutput.cs`:

```csharp compile=app file=Trains/LowStockCheck/LowStockCheckOutput.cs
namespace Shopfront.Trains.LowStockCheck;

public record LowStockCheckOutput(List<string> LowSkus);
```

`Trains/LowStockCheck/ILowStockCheckTrain.cs`:

```csharp compile=app file=Trains/LowStockCheck/ILowStockCheckTrain.cs
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.LowStockCheck;

public interface ILowStockCheckTrain : IServiceTrain<LowStockCheckInput, LowStockCheckOutput>;
```

`Trains/LowStockCheck/Junctions/FindLowStockJunction.cs`:

```csharp compile=app file=Trains/LowStockCheck/Junctions/FindLowStockJunction.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.LowStockCheck.Junctions;

internal class FindLowStockJunction(IShopfrontDbContext db)
    : EffectJunction<LowStockCheckInput, LowStockCheckOutput>
{
    public override async Task<LowStockCheckOutput> Run(LowStockCheckInput input) =>
        new(
            await db
                .Products.Where(p => p.Stock <= input.Threshold)
                .OrderBy(p => p.Sku)
                .Select(p => p.Sku)
                .ToListAsync(CancellationToken)
        );
}
```

`Trains/LowStockCheck/Junctions/NotifyStaffJunction.cs`:

```csharp compile=app file=Trains/LowStockCheck/Junctions/NotifyStaffJunction.cs
using Shopfront.Staff;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.LowStockCheck.Junctions;

internal class NotifyStaffJunction(IStaffNotifier staff) : EffectJunction<LowStockCheckOutput, Unit>
{
    public override async Task<Unit> Run(LowStockCheckOutput low)
    {
        if (low.LowSkus.Count > 0)
            await staff.Notify($"Low on {string.Join(", ", low.LowSkus)}.", CancellationToken);
        return Unit.Default;
    }
}
```

`Trains/LowStockCheck/LowStockCheckTrain.cs`:

```csharp compile=app file=Trains/LowStockCheck/LowStockCheckTrain.cs
using Shopfront.Trains.LowStockCheck.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.LowStockCheck;

public class LowStockCheckTrain
    : ServiceTrain<LowStockCheckInput, LowStockCheckOutput>,
        ILowStockCheckTrain
{
    protected override Task<Either<Exception, LowStockCheckOutput>> Junctions() =>
        Chain<FindLowStockJunction>().Chain<NotifyStaffJunction>().Resolve();
}
```

`NotifyStaffJunction` returns `Unit`, so Memory still holds the `LowStockCheckOutput` the first
junction made when the chain resolves, and that is the train's output.

The notifier is registered with the rest of the shop.

`Extensions/ShopfrontServiceCollectionExtensions.cs`:

```csharp compile=app file=Extensions/ShopfrontServiceCollectionExtensions.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Failures;
using Shopfront.Fraud;
using Shopfront.Inventory;
using Shopfront.Payments;
using Shopfront.Staff;
using Trax.Core.Decisions;
using Trax.Effect.Services.FailureClassifier;

namespace Shopfront.Extensions;

public static class ShopfrontServiceCollectionExtensions
{
    // Everything Shopfront's trains need, apart from Trax itself.
    public static IServiceCollection AddShopfront(
        this IServiceCollection services,
        string connectionString
    ) =>
        services
            // One context per scope, built for that scope's caller: a pool would share it.
            .AddDbContextFactory<ShopfrontDbContext>(
                options => options.UseNpgsql(connectionString),
                ServiceLifetime.Scoped
            )
            .AddScoped<IShopfrontDbContext>(sp =>
                sp.GetRequiredService<IDbContextFactory<ShopfrontDbContext>>().CreateDbContext()
            )
            .AddScoped<IShopfrontCaller, TraxShopfrontCaller>()
            .AddScoped<Catalogue>()
            .AddSingleton<IPaymentProvider, FakePaymentProvider>()
            .AddSingleton<IFailureClassifier, ShopfrontFailureClassifier>()
            .AddSingleton<IDecider>(FraudRules.Live())
            .AddSingleton<ICandidateFraudDecider, StricterFraudRules>()
            .AddSingleton<IStaffNotifier, LoggingStaffNotifier>();
}
```

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [Scheduling helpers](/docs/sdk-reference/scheduler-api/scheduling-helpers) | [Exclusions](/docs/scheduler/exclusions) | [Scheduling options](/docs/scheduler/scheduling-options) | [Dependent trains](/docs/scheduler/dependent-trains)

---

Next: [9.4 Schedule them](/docs/book/scheduled-work/schedule-them)
