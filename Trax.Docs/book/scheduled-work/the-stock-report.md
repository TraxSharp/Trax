---
title: "9.2 The stock report"
description: "Chapter 9 of the Trax book, section 2: The stock report."
type: tutorial
area: general
parent: "9. Scheduled work"
grand_parent: The Trax book
nav_order: 2
snippet_context: none
follows: book/scheduled-work/a-schedule-is-a-manifest
---

# 9.2 The stock report

The report is a train like any other. Its output is the report: the time it was taken and each
product's stock. The run's record keeps that output, so the report needs no table of its own.

By the end of the chapter the new trains are:

```text
Trains/
├── StockReport/
│   ├── StockReportInput.cs
│   ├── StockReportOutput.cs
│   ├── IStockReportTrain.cs
│   ├── StockReportTrain.cs
│   ├── Junctions/
│   │   └── CountStockJunction.cs
│   └── Models/
│       └── StockLine.cs
└── LowStockCheck/
    ├── LowStockCheckInput.cs
    ├── LowStockCheckOutput.cs
    ├── ILowStockCheckTrain.cs
    ├── LowStockCheckTrain.cs
    └── Junctions/
        ├── FindLowStockJunction.cs
        └── NotifyStaffJunction.cs
```

A scheduled train's input is stored with its manifest, so it implements `IManifestProperties`, the
marker Trax uses to tell an input it may store from any other object. The report needs no
settings, so its input is empty.

`Trains/StockReport/StockReportInput.cs`:

```csharp compile=app file=Trains/StockReport/StockReportInput.cs
using Trax.Effect.Models.Manifest;

namespace Shopfront.Trains.StockReport;

public record StockReportInput : IManifestProperties;
```

`Trains/StockReport/Models/StockLine.cs`:

```csharp compile=app file=Trains/StockReport/Models/StockLine.cs
namespace Shopfront.Trains.StockReport.Models;

public record StockLine(string Sku, int Stock);
```

`StockLine` is part of the output, so it is public.

`Trains/StockReport/StockReportOutput.cs`:

```csharp compile=app file=Trains/StockReport/StockReportOutput.cs
using Shopfront.Trains.StockReport.Models;

namespace Shopfront.Trains.StockReport;

public record StockReportOutput(DateTime TakenAt, int Units, List<StockLine> Lines);
```

`Trains/StockReport/IStockReportTrain.cs`:

```csharp compile=app file=Trains/StockReport/IStockReportTrain.cs
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.StockReport;

public interface IStockReportTrain : IServiceTrain<StockReportInput, StockReportOutput>;
```

`Trains/StockReport/Junctions/CountStockJunction.cs`:

```csharp compile=app file=Trains/StockReport/Junctions/CountStockJunction.cs
using Microsoft.EntityFrameworkCore;
using Shopfront.Data;
using Shopfront.Trains.StockReport.Models;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.StockReport.Junctions;

internal class CountStockJunction(IShopfrontDbContext db, ILogger<CountStockJunction> logger)
    : EffectJunction<StockReportInput, StockReportOutput>
{
    public override async Task<StockReportOutput> Run(StockReportInput input)
    {
        var lines = await db
            .Products.OrderBy(p => p.Sku)
            .Select(p => new StockLine(p.Sku, p.Stock))
            .ToListAsync(CancellationToken);

        var report = new StockReportOutput(DateTime.UtcNow, lines.Sum(l => l.Stock), lines);
        logger.LogInformation(
            "Counted {Units} units across {Products} products",
            report.Units,
            lines.Count
        );
        return report;
    }
}
```

`Trains/StockReport/StockReportTrain.cs`:

```csharp compile=app file=Trains/StockReport/StockReportTrain.cs
using Shopfront.Trains.StockReport.Junctions;
using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;

namespace Shopfront.Trains.StockReport;

public class StockReportTrain : ServiceTrain<StockReportInput, StockReportOutput>, IStockReportTrain
{
    protected override Task<Either<Exception, StockReportOutput>> Junctions() =>
        Chain<CountStockJunction>().Resolve();
}
```

The train carries no `[TraxAuthorize]` and no GraphQL attribute. Nobody asks for it: the scheduler
runs it, as trusted infrastructure, and it is not part of the API.

A scheduled run has no caller. The products have no owner, so the report reads them all; a train
that read orders would see none, because the query filter from chapter 6 shows a context with no
caller nothing. That is the filter failing closed, and it is why the report counts products rather
than adding up orders.

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [Scheduling helpers](/docs/sdk-reference/scheduler-api/scheduling-helpers) | [Exclusions](/docs/scheduler/exclusions) | [Scheduling options](/docs/scheduler/scheduling-options) | [Dependent trains](/docs/scheduler/dependent-trains)

---

Next: [9.3 The low-stock check](/docs/book/scheduled-work/the-low-stock-check)
