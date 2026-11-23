using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.AuthE2E;

public record NotifyInput : IManifestProperties
{
    public required string Topic { get; init; }
    public required string Body { get; init; }
}
