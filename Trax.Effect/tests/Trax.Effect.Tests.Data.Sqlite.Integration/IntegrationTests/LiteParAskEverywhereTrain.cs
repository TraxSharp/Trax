using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>Asks one question before the fork, in each of two branches, and after the join.</summary>
public class LiteParAskEverywhereTrain
    : ServiceTrain<LiteParOrder, string>,
        ILiteParAskEverywhereTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Switch<LiteParOrder, LiteParLane>(Lanes)
            .Parallel(p =>
                p.Branch("a", b => b.Switch<LiteParOrder, LiteParLane>(Lanes))
                    .Branch("b", b => b.Switch<LiteParOrder, LiteParLane>(Lanes))
            )
            .Switch<LiteParOrder, LiteParLane>(Lanes)
            .Chain<LiteParFinish>()
            .Resolve();

    private static Tracks<LiteParOrder, string, LiteParLane> Lanes(
        Tracks<LiteParOrder, string, LiteParLane> tracks
    ) =>
        tracks
            .When(LiteParLane.Express, t => t.Chain<LiteParTookExpress>())
            .When(LiteParLane.Ground, t => t.Chain<LiteParTookGround>());
}
