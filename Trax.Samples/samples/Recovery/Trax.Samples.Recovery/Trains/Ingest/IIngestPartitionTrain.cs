using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Recovery.Trains.Ingest;

public interface IIngestPartitionTrain : IServiceTrain<IngestPartitionInput, IngestPartitionResult>;
