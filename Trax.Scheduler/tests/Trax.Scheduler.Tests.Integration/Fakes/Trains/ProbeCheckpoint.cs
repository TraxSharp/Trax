using System.Collections.Concurrent;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

public class ProbeCheckpoint : Junction<ProbeLaneTaken, ProbeChecked>
{
    public override Task<ProbeChecked> Run(ProbeLaneTaken input) =>
        DecisionProbe.FailAt == ProbeFailure.BetweenQuestions
            ? throw new InvalidOperationException("The probe failed between its questions.")
            : Task.FromResult(new ProbeChecked(input.Value));
}
