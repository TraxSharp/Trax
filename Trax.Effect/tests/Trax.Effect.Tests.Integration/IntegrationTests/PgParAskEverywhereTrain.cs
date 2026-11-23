using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>Asks one question before the fork, in each of two branches, and after the join.</summary>
public class PgParAskEverywhereTrain : ServiceTrain<PgParOrder, string>, IPgParAskEverywhereTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Switch<PgParOrder, PgParLane>(Lanes)
            .Parallel(p =>
                p.Branch("a", b => b.Switch<PgParOrder, PgParLane>(Lanes))
                    .Branch("b", b => b.Switch<PgParOrder, PgParLane>(Lanes))
            )
            .Switch<PgParOrder, PgParLane>(Lanes)
            .Chain<PgParFinish>()
            .Resolve();

    private static Tracks<PgParOrder, string, PgParLane> Lanes(
        Tracks<PgParOrder, string, PgParLane> tracks
    ) =>
        tracks
            .When(PgParLane.Express, t => t.Chain<PgParTookExpress>())
            .When(PgParLane.Ground, t => t.Chain<PgParTookGround>());
}
