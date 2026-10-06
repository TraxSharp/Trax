using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Recovery.Trains.Refund;

public interface IApproveRefundTrain : IServiceTrain<RefundInput, RefundResult>;
