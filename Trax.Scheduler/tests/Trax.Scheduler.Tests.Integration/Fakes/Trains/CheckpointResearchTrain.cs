using System.Collections.Concurrent;
using System.Globalization;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Tests.Integration.Fakes.Trains;

/// <summary>
/// The Recovery sample's research chain with a checkpoint:
/// <c>PlanResearch → WriteResearchNote → Switch&lt;ResearchBrief, ResearchDepth&gt; →
/// FetchFullTexts → Checkpoint&lt;CheckedFindings&gt; → Switch&lt;CheckedFindings,
/// SummaryStyle&gt; → Summarize</c>. <see cref="ResearchProbe"/> records which junctions ran and
/// arms a crash.
/// </summary>
/// <remarks>
/// Every host built over this test assembly registers it, so a host that starts with chain
/// verification on also needs an <see cref="IDecider"/>, as <see cref="DecisionProbeTrain"/> does.
/// </remarks>
public class CheckpointResearchTrain : ServiceTrain<ResearchInput, string>, ICheckpointResearchTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PlanResearch>()
            .Chain<WriteResearchNote>()
            .Switch<ResearchBrief, ResearchDepth>(tracks =>
                tracks
                    .When(ResearchDepth.Quick, t => t.Chain<SearchQuick>())
                    .When(ResearchDepth.Deep, t => t.Chain<SearchDeep>())
            )
            .Chain<FetchFullTexts>()
            .Checkpoint<CheckedFindings>()
            .Switch<CheckedFindings, SummaryStyle>(tracks =>
                tracks
                    .When(SummaryStyle.Short, t => t.Chain<SummarizeShort>())
                    .When(SummaryStyle.Long, t => t.Chain<SummarizeLong>())
            )
            .Resolve();
}
