using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Recovery.Trains.PartitionAction;

public interface IPartitionActionTrain : IServiceTrain<PartitionActionInput, PartitionActionOutput>;
