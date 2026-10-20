using Microsoft.Extensions.DependencyInjection;
using Shopfront.Couriers;
using Shopfront.Extensions;
using Shopfront.Trains.FulfilOrder;
using Shopfront.Warehouse;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class FulfilOrderTrainTests
{
    private ServiceProvider _provider = null!;

    [SetUp]
    public void BuildTheContainer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddShopfront(TestDatabase.ConnectionString);
        // Fakes that answer at once, registered after AddShopfront's, so they win.
        services.AddSingleton<IWarehouse>(
            new FakeWarehouse { ChoosingTime = TimeSpan.Zero, PackingTime = TimeSpan.Zero }
        );
        services.AddSingleton<ICourier>(new FakeCourier { QuoteTime = TimeSpan.Zero });
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddMediator(typeof(IFulfilOrderTrain).Assembly)
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public ValueTask DisposeTheContainer() => _provider.DisposeAsync();

    [Test]
    public async Task RunAsync_JoinsTheBranchesAndPacksUnderTheLabel()
    {
        var orderId = Guid.Parse("3179f9c4-07ba-4f64-ac64-e7026995cc89");
        var bus = _provider.GetRequiredService<ITrainBus>();

        var parcel = await bus.RunAsync<FulfilOrderOutput>(
            new FulfilOrderInput { OrderId = orderId, Items = 3 }
        );

        parcel.Should().Be(new FulfilOrderOutput(orderId, "TRK-3179F9C4"));
    }
}
