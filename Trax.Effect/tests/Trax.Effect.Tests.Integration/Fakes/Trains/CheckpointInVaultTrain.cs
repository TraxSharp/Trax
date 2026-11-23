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

public sealed class CheckpointInVaultTrain : ServiceTrain<string, string>, ICheckpointInVaultTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PlanResearch>()
            .Chain<SearchWebFromPlan>()
            .Switch<Findings, Vault>(s =>
                s.When(
                        Vault.Left,
                        l =>
                            l.Chain<OpenVaultLeft>()
                                .Chain<FetchFullTexts>()
                                .Checkpoint<CheckedFindings>()
                    )
                    .When(Vault.Right, r => r.Chain<OpenVaultRight>().Chain<FetchFullTexts>())
            )
            .Chain<ScoreFindings>()
            .Chain<Summarize>()
            .Resolve();
}
