using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Mediator.Services.TrainBus;
using Trax.Samples.Scheduling.E2E.Fixtures;
using Trax.Samples.Scheduling.Trains.RefreshExchangeRates;

namespace Trax.Samples.Scheduling.E2E.SchedulingTests;

/// <summary>
/// A scheduled train is still an ordinary train: the train bus runs it directly, outside any
/// manifest.
/// </summary>
public class TrainBusTests : SchedulingTestFixture
{
    [Test]
    public async Task Scheduled_train_runs_on_the_train_bus_and_returns_its_output()
    {
        using var scope = Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ITrainBus>();
        RateFeed.SpikesEnabled = false;

        try
        {
            var output = await bus.RunAsync<RefreshExchangeRatesOutput>(
                new RefreshExchangeRatesInput { BaseCurrency = "EUR" }
            );

            output.BaseCurrency.Should().Be("EUR");
            output.SpikeDetected.Should().BeFalse();
        }
        finally
        {
            RateFeed.SpikesEnabled = true;
        }
    }

    [Test]
    public async Task Dormant_activation_outside_a_scheduled_run_does_nothing()
    {
        using var scope = Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<ITrainBus>();
        var alert = await Db.Manifest(ManifestNames.AlertRateSpike);
        // A threshold of zero makes any move a spike, so the junction asks to activate.
        var output = await bus.RunAsync<RefreshExchangeRatesOutput>(
            new RefreshExchangeRatesInput { BaseCurrency = "GBP", SpikeThresholdPercent = 0m }
        );

        output.SpikeDetected.Should().BeTrue();

        // The context has no manifest to activate under, so it logs a warning and queues nothing.
        var alertEntries = await Db.Query(dc =>
            dc.WorkQueues.AsNoTracking().Where(w => w.ManifestId == alert.Id).ToListAsync()
        );
        alertEntries.Should().NotContain(w => w.Input != null && w.Input.Contains("GBP"));
    }
}
