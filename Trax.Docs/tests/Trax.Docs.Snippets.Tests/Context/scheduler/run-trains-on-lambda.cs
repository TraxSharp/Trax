// What the examples on scheduler/run-trains-on-lambda.md take as given: the train they route.
using Trax.Core.Functional;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

public record ContentInput : IManifestProperties
{
    public string ContentId { get; init; } = "";
}

public interface IReviewContentTrain : IServiceTrain<ContentInput, Unit>;
