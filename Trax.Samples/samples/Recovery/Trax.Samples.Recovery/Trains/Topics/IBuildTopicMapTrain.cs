using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Recovery.Trains.Topics;

public interface IBuildTopicMapTrain : IServiceTrain<TopicMapInput, TopicMap>;
