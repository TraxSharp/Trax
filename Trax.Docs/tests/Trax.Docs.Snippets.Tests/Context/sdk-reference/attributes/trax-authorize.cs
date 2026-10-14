// What the example on sdk-reference/attributes/trax-authorize.md names but does not declare.
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

public record RefundInput(long OrderId);

public record RefundResult(long RefundId);

public interface IRefundOrderTrain : IServiceTrain<RefundInput, RefundResult>;

public class IssueRefund : Junction<RefundInput, RefundResult>
{
    public override Task<RefundResult> Run(RefundInput input) =>
        Task.FromResult(new RefundResult(input.OrderId));
}
