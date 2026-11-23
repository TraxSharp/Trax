using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>Produces the typed output these scheduler tests assert on.</summary>
internal sealed class ProcessTypedOutput : Junction<TypedOutputSchedulerTestInput, string>
{
    public override Task<string> Run(TypedOutputSchedulerTestInput input) =>
        Task.FromResult($"processed-{input.Value}");
}
