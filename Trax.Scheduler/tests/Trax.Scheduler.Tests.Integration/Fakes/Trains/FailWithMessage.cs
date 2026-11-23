using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>Fails with the message the input carries, so the failure path is exercised.</summary>
internal sealed class FailWithMessage : Junction<FailingSchedulerTestInput, Unit>
{
    public override Task<Unit> Run(FailingSchedulerTestInput input) =>
        throw new InvalidOperationException($"Intentional failure: {input.FailureMessage}");
}
