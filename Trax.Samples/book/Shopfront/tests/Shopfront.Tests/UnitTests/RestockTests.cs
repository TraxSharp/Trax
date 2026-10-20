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
