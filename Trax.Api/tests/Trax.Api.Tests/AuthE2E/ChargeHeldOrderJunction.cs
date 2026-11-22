using Trax.Core.Junction;

namespace Trax.Api.Tests.AuthE2E;

internal sealed class ChargeHeldOrderJunction : Junction<StockHold, PlaceOrderOutput>
{
    public override Task<PlaceOrderOutput> Run(StockHold input) =>
        throw new InvalidOperationException("Card declined.");
}
