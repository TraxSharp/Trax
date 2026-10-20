using Shopfront.Data;
using Shopfront.Trains.PlaceOrder.Models;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.PlaceOrder.Junctions;

internal class IdentifyCustomerJunction(IShopfrontCaller caller)
    : EffectJunction<PlaceOrderInput, Customer>
{
    public override Task<Customer> Run(PlaceOrderInput input) =>
        Task.FromResult(
            new Customer(
                caller.CustomerId ?? throw new TrainException("Sign in to place an order.")
            )
        );
}
