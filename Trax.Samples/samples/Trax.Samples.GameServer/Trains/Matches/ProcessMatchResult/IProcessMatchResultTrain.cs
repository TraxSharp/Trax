using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult;

public interface IProcessMatchResultTrain
    : IServiceTrain<ProcessMatchResultInput, ProcessMatchResultOutput>;
