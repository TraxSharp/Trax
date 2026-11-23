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

public sealed class DirtyResearchTrain : ServiceTrain<string, string>, IDirtyResearchTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PlanResearch>()
            .Chain<SearchWebFromPlan>()
            .Chain<FetchFullTexts>()
            .Chain<MakeDirty>()
            .Checkpoint<CheckedFindings>()
            .Chain<ScoreFindings>()
            .Chain<Summarize>()
            .Resolve();
}
