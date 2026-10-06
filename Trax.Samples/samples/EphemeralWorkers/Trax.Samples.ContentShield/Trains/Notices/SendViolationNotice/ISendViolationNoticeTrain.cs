using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.ContentShield.Trains.Notices.SendViolationNotice;

public interface ISendViolationNoticeTrain
    : IServiceTrain<SendViolationNoticeInput, SendViolationNoticeOutput>;
