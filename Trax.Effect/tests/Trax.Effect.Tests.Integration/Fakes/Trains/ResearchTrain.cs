using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.Fakes.Trains;

public sealed class ResearchTrain : ServiceTrain<string, string>, IResearchTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PlanResearch>()
            .Switch<Brief, ResearchSource>(s =>
                s.When(ResearchSource.Web, w => w.Chain<SearchWeb>())
                    .When(ResearchSource.Papers, p => p.Chain<SearchPapers>())
            )
            .Chain<FetchFullTexts>()
            .Checkpoint<CheckedFindings>()
            .Chain<ScoreFindings>()
            .Checkpoint<Scored>()
            .Chain<Summarize>()
            .Resolve();
}
