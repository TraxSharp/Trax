---
title: "9.6 Test it"
description: "Chapter 9 of the Trax book, section 6: Test it."
type: tutorial
area: general
parent: "9. Scheduled work"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/scheduled-work/run-it-now
---

# 9.6 Test it

The junctions are tested against the test database, as the others are.

`tests/Shopfront.Tests/UnitTests/StockJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/StockJunctionTests.cs
using Shopfront.Staff;
using Shopfront.Trains.LowStockCheck;
using Shopfront.Trains.LowStockCheck.Junctions;
using Shopfront.Trains.StockReport;
using Shopfront.Trains.StockReport.Junctions;
using Shopfront.Trains.StockReport.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class StockJunctionTests
{
    [SetUp]
    public Task ResetTheDatabase() => TestDatabase.Reset();

    private sealed class RecordingNotifier : IStaffNotifier
    {
        public List<string> Messages { get; } = [];

        public Task Notify(string message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task CountStock_CountsEveryProduct()
    {
        await using var db = TestDatabase.NewContext();

        var report = await new CountStockJunction(db, NullLogger<CountStockJunction>.Instance).Run(
            new StockReportInput()
        );

        report.Lines.Should().Equal(new StockLine("CAP", 0), new StockLine("MUG", 10), new StockLine("TEE", 3));
        report.Units.Should().Be(13);
    }

    [Test]
    public async Task FindLowStock_ListsProductsAtOrBelowTheThreshold()
    {
        await using var db = TestDatabase.NewContext();

        var low = await new FindLowStockJunction(db).Run(new LowStockCheckInput { Threshold = 3 });

        low.LowSkus.Should().Equal("CAP", "TEE");
    }

    [Test]
    public async Task NotifyStaff_NamesTheLowProducts()
    {
        var staff = new RecordingNotifier();

        await new NotifyStaffJunction(staff).Run(new LowStockCheckOutput(["CAP", "TEE"]));

        staff.Messages.Should().Equal("Low on CAP, TEE.");
    }

    [Test]
    public async Task NotifyStaff_NothingLow_SaysNothing()
    {
        var staff = new RecordingNotifier();

        await new NotifyStaffJunction(staff).Run(new LowStockCheckOutput([]));

        staff.Messages.Should().BeEmpty();
    }
}
```

The host test checks what the host declares: both manifests, the check after the report.

`tests/Shopfront.Tests/IntegrationTests/ScheduleTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/ScheduleTests.cs
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Auth;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class ScheduleTests
{
    private WebApplicationFactory<Program> _app = null!;

    [SetUp]
    public async Task StartTheApplication()
    {
        await TestDatabase.Reset();
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseEnvironment("Development")
                .UseSetting("ConnectionStrings:Shopfront", TestDatabase.ConnectionString)
        );
    }

    [TearDown]
    public ValueTask StopTheApplication() => _app.DisposeAsync();

    [Test]
    public async Task Manifests_TheReportAndTheCheckAreScheduled()
    {
        var client = _app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            DevJwt.Mint("lin", Roles.Staff)
        );

        var response = await client.Post(
            "{ operations { manifests(take: 10) { items { externalId cronExpression } } } }"
        );

        var manifests = response
            .GetProperty("data")
            .GetProperty("operations")
            .GetProperty("manifests")
            .GetProperty("items")
            .EnumerateArray()
            .ToDictionary(
                m => m.GetProperty("externalId").GetString()!,
                m => m.GetProperty("cronExpression").GetString()
            );
        manifests.Should().ContainKey(ManifestNames.StockReport).WhoseValue.Should().Be("0 2 * * *");
        manifests.Should().ContainKey(ManifestNames.LowStockCheck);
    }
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    60, Skipped:     0, Total:    60, Duration: 55 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Shopfront counts its stock every night and tells staff what is running low, without anyone asking,
and leaves the shop's holidays alone. Staff can see each schedule and each run in the console, and
run either now.

Each of those runs succeeded. The next chapter adds work that fails: a supplier's stock feed that
is sometimes down, retried, given up on, and picked up again by staff.

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [Scheduling helpers](/docs/sdk-reference/scheduler-api/scheduling-helpers) | [Exclusions](/docs/scheduler/exclusions) | [Scheduling options](/docs/scheduler/scheduling-options) | [Dependent trains](/docs/scheduler/dependent-trains)

---

Next: [10. Failures, retries and dead letters](/docs/book/failures-retries-and-dead-letters)
