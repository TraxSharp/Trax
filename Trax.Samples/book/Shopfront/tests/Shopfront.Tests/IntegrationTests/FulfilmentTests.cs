using Microsoft.Extensions.DependencyInjection;
using Shopfront.Auth;
using Shopfront.Data;
using Shopfront.Extensions;
using Shopfront.Trains.FulfilOrder;
using Shopfront.Trains.PlaceOrder;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Api.Auth;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainBus;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class FulfilmentTests
{
    private ServiceProvider _provider = null!;

    [SetUp]
    public async Task BuildTheContainer()
    {
        await TestDatabase.Reset();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddShopfront(TestDatabase.ConnectionString);
        services.AddScoped(_ => new TraxPrincipal("TraxJwt:ada", "ada", [Roles.Customer]));
        services.AddScoped<IShopfrontCaller>(_ => TestCaller.Customer("TraxJwt:ada"));
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory().SaveTrainParameters())
                .AddMediator(typeof(IPlaceOrderTrain).Assembly)
        );
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public ValueTask DisposeTheContainer() => _provider.DisposeAsync();

    [Test]
    public async Task PlaceOrder_Confirmed_QueuesItsFulfilment()
    {
        var bus = _provider.GetRequiredService<ITrainBus>();

        var order = await bus.RunAsync<PlaceOrderOutput>(
            new PlaceOrderInput { Lines = [new OrderLine("MUG", 2)], PaymentToken = "tok_visa" }
        );

        using var scope = _provider.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var queued = data.WorkQueues.Single();
        queued.TrainName.Should().Be(typeof(IFulfilOrderTrain).FullName);
        queued.Input.Should().Contain(order.OrderId.ToString());
    }
}
