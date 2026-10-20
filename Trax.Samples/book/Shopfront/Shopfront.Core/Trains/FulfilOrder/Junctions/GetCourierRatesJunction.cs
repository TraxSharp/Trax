using Shopfront.Couriers;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.FulfilOrder.Junctions;

internal class GetCourierRatesJunction(ICourier courier, ILogger<GetCourierRatesJunction> logger)
    : EffectJunction<FulfilOrderInput, CourierRates>
{
    public override async Task<CourierRates> Run(FulfilOrderInput input)
    {
        logger.LogInformation("Asking the courier's rates for order {OrderId}", input.OrderId);
        var rates = await courier.Rates(CancellationToken);
        logger.LogInformation(
            "The courier charges {Base} and {PerKilogram} a kilogram",
            rates.Base,
            rates.PerKilogram
        );
        return rates;
    }
}
