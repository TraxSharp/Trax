// What the examples on scheduler/run-trains-on-sqs-workers.md take as given: the train they route.
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

public record BatchInput : IManifestProperties
{
    public string BatchId { get; init; } = "";
}

public interface IBatchProcessTrain : IServiceTrain<BatchInput, Unit>;
