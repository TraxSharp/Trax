using Trax.Effect.Enums;
using Trax.Samples.EnergyHub.E2E.Fixtures;
using Trax.Samples.EnergyHub.E2E.Utilities;
using Trax.Samples.EnergyHub.Trains.ChargingSessions.ProcessChargingSession;
using Trax.Samples.EnergyHub.Trains.GridTrading.TradeGridEnergy;
using Trax.Samples.EnergyHub.Trains.SolarProduction.MonitorSolarProduction;
using Trax.Samples.EnergyHub.Trains.Sustainability.GenerateSustainabilityReport;

namespace Trax.Samples.EnergyHub.E2E.HubTests;

[TestFixture]
public class TrainCompletionTests : HubTestFixture
{
    [Test]
    public async Task MonitorSolarProduction_ReturnsOutput()
    {
        var output = await TrainBus.RunAsync<MonitorSolarProductionOutput>(
            new MonitorSolarProductionInput { ArrayId = "SPA-001", Region = "somerset" }
        );

        output.ArrayId.Should().Be("SPA-001");
        output.TotalKwh.Should().BeGreaterThan(0);

        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "MonitorSolarProduction",
            TrainState.Completed,
            TimeSpan.FromSeconds(5)
        );

        metadata.Should().NotBeNull();
    }

    [Test]
    public async Task GenerateSustainabilityReport_ReturnsMetrics()
    {
        var output = await TrainBus.RunAsync<GenerateSustainabilityReportOutput>(
            new GenerateSustainabilityReportInput { ReportPeriod = "Daily" }
        );

        output.ReportPeriod.Should().Be("Daily");
        output.CarbonOffsetTons.Should().BeGreaterThanOrEqualTo(0);
        output.RenewablePercent.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task ProcessChargingSession_ReturnsSessionData()
    {
        var output = await TrainBus.RunAsync<ProcessChargingSessionOutput>(
            new ProcessChargingSessionInput { StationId = "EVC-PLAZA", SessionType = "Wired" }
        );

        output.SessionsProcessed.Should().BeGreaterThan(0);
        output.RevenueGenerated.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task TradeGridEnergy_ReturnsTradeResult()
    {
        var output = await TrainBus.RunAsync<TradeGridEnergyOutput>(
            new TradeGridEnergyInput { RatePerKwh = 0.14m, MaxSellPercent = 80 }
        );

        output.UbossTransactionId.Should().NotBeNullOrEmpty();
    }
}
