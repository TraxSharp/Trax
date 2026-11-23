using Trax.Core.Junction;

namespace Trax.Api.Tests.AuthE2E;

internal sealed class HoldStockJunction : Junction<PlaceOrderInput, StockHold>
{
    public override Task<StockHold> Run(PlaceOrderInput input) =>
        Task.FromResult(new StockHold { Sku = input.Sku });
}
