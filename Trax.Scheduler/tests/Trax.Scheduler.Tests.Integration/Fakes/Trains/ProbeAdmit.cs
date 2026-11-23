using System.Collections.Concurrent;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

public class ProbeAdmit : Junction<DecisionProbeInput, ProbeAdmitted>
{
    public override Task<ProbeAdmitted> Run(DecisionProbeInput input) =>
        DecisionProbe.FailAt == ProbeFailure.BeforeFirstQuestion
            ? throw new InvalidOperationException("The probe failed before its first question.")
            : Task.FromResult(new ProbeAdmitted(input.Value));
}
