using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Recovery.Trains.ChangeCaseData;

public interface IChangeCaseDataTrain : IServiceTrain<ChangeCaseDataInput, ChangeCaseDataOutput>;
