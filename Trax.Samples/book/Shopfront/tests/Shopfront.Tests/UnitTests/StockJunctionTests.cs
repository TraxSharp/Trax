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
