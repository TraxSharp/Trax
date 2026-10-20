using Shopfront.Staff;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;

namespace Shopfront.Trains.LowStockCheck.Junctions;

internal class NotifyStaffJunction(IStaffNotifier staff) : EffectJunction<LowStockCheckOutput, Unit>
{
    public override async Task<Unit> Run(LowStockCheckOutput low)
    {
        if (low.LowSkus.Count > 0)
            await staff.Notify($"Low on {string.Join(", ", low.LowSkus)}.", CancellationToken);
        return Unit.Default;
    }
}
