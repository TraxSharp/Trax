using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Records;

namespace Trax.Samples.Recovery.Trains.Refund.Junctions;

/// <summary>
/// Reads the order the refund is for. The case holds everything the approval model reads, because the
/// state hash covers only the state: a value the model looked up elsewhere would not be noticed when
/// it changed.
/// </summary>
public class LoadRefundCase(CaseFiles caseFiles, DemoPace pace)
    : EffectJunction<RefundInput, RefundCase>
{
    public override async Task<RefundCase> Run(RefundInput input)
    {
        await Task.Delay(pace.StepDelay);
        var order = caseFiles.OrderFor(input.RunId);
        return new RefundCase
        {
            RunId = input.RunId,
            OrderId = order.OrderId,
            Amount = order.Amount,
            Reason = order.Reason,
            PriorRefunds = order.PriorRefunds,
            CustomerEmail = order.CustomerEmail,
        };
    }
}
