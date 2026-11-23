using System.Collections.Concurrent;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

public class ProbeSmall : Junction<DecisionProbeInput, string>
{
    public override Task<string> Run(DecisionProbeInput input)
    {
        DecisionProbe.Taken.Enqueue((input.Value, nameof(ProbeSize.Small)));
        return DecisionProbe.FailAt == ProbeFailure.AfterQuestions
            ? throw new InvalidOperationException("The probe failed after its questions.")
            : Task.FromResult(nameof(ProbeSize.Small));
    }
}
