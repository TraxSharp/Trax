---
title: "10.8 Test it"
description: "Chapter 10 of the Trax book, section 8: Test it."
type: tutorial
area: general
parent: "10. Failures, retries and dead letters"
grand_parent: The Trax book
nav_order: 8
snippet_context: none
follows: book/failures-retries-and-dead-letters/bring-it-back
---

# 10.8 Test it

The junction is tested against a supplier that is down and one that is not. The hook is tested
on its own.

`tests/Shopfront.Tests/UnitTests/RestockTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/RestockTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Shopfront.Staff;
using Shopfront.Suppliers;
using Shopfront.Trains.Restock.Junctions;
using Shopfront.Trains.Restock.Models;
using Trax.Effect.Models.Metadata;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class RestockTests
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

    private static ReceiveDeliveryJunction Junction(
        FakeSupplier supplier,
        Shopfront.Data.ShopfrontDbContext db
    ) => new(supplier, db, NullLogger<ReceiveDeliveryJunction>.Instance);

    [Test]
    public async Task ReceiveDelivery_SupplierUp_AddsACartonToEachShortProduct()
    {
        await using var db = TestDatabase.NewContext();

        var output = await Junction(new FakeSupplier(), db).Run(new ShortProducts(["CAP", "TEE"]));

        output.Delivered.Should().Equal(new Delivery("CAP", 5), new Delivery("TEE", 5));
        (await TestDatabase.StockOf("CAP")).Should().Be(5);
        (await TestDatabase.StockOf("TEE")).Should().Be(8);
    }

    [Test]
    public async Task ReceiveDelivery_SupplierDown_ThrowsAndChangesNothing()
    {
        await using var db = TestDatabase.NewContext();

        var act = () => Junction(new FakeSupplier { IsDown = true }, db).Run(new ShortProducts(["CAP"]));

        await act.Should().ThrowAsync<SupplierUnavailableException>();
        (await TestDatabase.StockOf("CAP")).Should().Be(0);
    }

    [Test]
    public async Task StaffFailureHook_ScheduledRun_TellsStaff()
    {
        var staff = new RecordingNotifier();
        var metadata = new Metadata { Name = "Shopfront.Trains.Restock.IRestockTrain", ManifestId = 3 };

        await new StaffFailureHook(staff).OnFailed(
            metadata,
            new SupplierUnavailableException("The supplier's feed did not answer."),
            CancellationToken.None
        );

        staff
            .Messages.Should()
            .Equal(
                "Shopfront.Trains.Restock.IRestockTrain failed: The supplier's feed did not answer."
            );
    }

    [Test]
    public async Task StaffFailureHook_RunOnARequest_SaysNothing()
    {
        var staff = new RecordingNotifier();

        await new StaffFailureHook(staff).OnFailed(
            new Metadata { Name = "Shopfront.Trains.PlaceOrder.IPlaceOrderTrain" },
            new InvalidOperationException(),
            CancellationToken.None
        );

        staff.Messages.Should().BeEmpty();
    }
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    64, Skipped:     0, Total:    64, Duration: 58 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Shopfront restocks from a supplier that goes down, and copes: each failure is recorded and
classified, the restock is tried twice more with a growing delay, and after the third failure it
stops and waits for a person, who is told and who sends it round again from the console once the
supplier is back.

Everything still runs in one process, the one customers are talking to. The next chapter moves the
work nobody waits for onto a process of its own.

## SDK Reference

> [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [How retries work](/docs/scheduler/how-retries-work) | [Resolve a dead letter](/docs/scheduler/dead-letters-and-cleanup) | [AddLifecycleHook](/docs/sdk-reference/configuration/add-lifecycle-hook)

---

Next: [11. A separate worker](/docs/book/a-separate-worker)
