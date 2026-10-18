---
title: "10.4 Tell staff when scheduled work fails"
description: "Chapter 10 of the Trax book, section 4: Tell staff when scheduled work fails."
type: tutorial
area: general
parent: "10. Failures, retries and dead letters"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/failures-retries-and-dead-letters/the-restock
---

# 10.4 Tell staff when scheduled work fails

A lifecycle hook runs on a run's transitions: started, completed, failed, cancelled. One that
reacts to a failure is how staff hear about a scheduled run nobody was watching. It runs for every
train, so it picks out the ones a manifest ran.

`Staff/StaffFailureHook.cs`:

```csharp compile=app file=Staff/StaffFailureHook.cs
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;

namespace Shopfront.Staff;

// Tells staff about every failed run of scheduled work. A failed order is the customer's to see.
public class StaffFailureHook(IStaffNotifier staff) : ITrainLifecycleHook
{
    public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
        metadata.ManifestId is null
            ? Task.CompletedTask
            : staff.Notify($"{metadata.Name} failed: {exception.Message}", ct);
}
```

A hook's failure is logged and never fails the run it was told about.

The supplier joins the shop's services, once, so the Development switch and the train see the same
one.

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
using Shopfront.Suppliers;
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
            .AddSingleton<IStaffNotifier, LoggingStaffNotifier>()
            .AddSingleton<FakeSupplier>()
            .AddSingleton<ISupplier>(sp => sp.GetRequiredService<FakeSupplier>());
}
```

## SDK Reference

> [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [How retries work](/docs/scheduler/how-retries-work) | [Resolve a dead letter](/docs/scheduler/dead-letters-and-cleanup) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)

---

Next: [10.5 Schedule it, with retries](/docs/book/failures-retries-and-dead-letters/schedule-it-with-retries)
