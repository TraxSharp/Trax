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

public sealed class TwoBranchTrain : ServiceTrain<string, string>, ITwoBranchTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PlanResearch>()
            .Parallel(p =>
                p.Branch("a", b => b.Chain<StartBranchA>().Chain<MeetA>().Checkpoint<BranchA>())
                    .Branch("b", b => b.Chain<StartBranchB>().Chain<MeetB>().Checkpoint<BranchB>())
            )
            .Chain<Join>()
            .Resolve();
}
