using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Recovery.Trains.Research;

public interface IResearchTopicTrain : IServiceTrain<ResearchInput, ResearchReport>;
