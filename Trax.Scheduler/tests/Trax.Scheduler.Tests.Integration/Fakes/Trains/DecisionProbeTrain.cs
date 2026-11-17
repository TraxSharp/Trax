using System.Collections.Concurrent;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>
/// A train that asks a decider two questions, one per routing step, and records each track it
/// takes, so a test can tell a replayed run from one that asked afresh.
/// </summary>
/// <remarks>
/// Its chain can be made to fail before the first question, between the two, or after both
/// (<see cref="DecisionProbe.FailAt"/>), to leave a run that recorded none, one or both of its
/// decisions. Every host built over this test assembly registers it, so a host that starts with
/// chain verification on also needs an <see cref="IDecider"/>.
/// </remarks>
public class DecisionProbeTrain : ServiceTrain<DecisionProbeInput, string>, IDecisionProbeTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<ProbeAdmit>()
            .Switch<DecisionProbeInput, ProbeLane>(tracks =>
                tracks
                    .When(ProbeLane.Fast, t => t.Chain<ProbeFastLane>())
                    .When(ProbeLane.Slow, t => t.Chain<ProbeSlowLane>())
            )
            .Chain<ProbeCheckpoint>()
            .Switch<DecisionProbeInput, ProbeSize>(tracks =>
                tracks
                    .When(ProbeSize.Small, t => t.Chain<ProbeSmall>())
                    .When(ProbeSize.Large, t => t.Chain<ProbeLarge>())
            )
            .Resolve();
}
