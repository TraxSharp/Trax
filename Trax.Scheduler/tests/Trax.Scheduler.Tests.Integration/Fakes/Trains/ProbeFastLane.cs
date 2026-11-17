using System.Collections.Concurrent;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

public class ProbeFastLane : Junction<DecisionProbeInput, ProbeLaneTaken>
{
    public override Task<ProbeLaneTaken> Run(DecisionProbeInput input)
    {
        DecisionProbe.Taken.Enqueue((input.Value, nameof(ProbeLane.Fast)));
        return Task.FromResult(new ProbeLaneTaken(input.Value));
    }
}
