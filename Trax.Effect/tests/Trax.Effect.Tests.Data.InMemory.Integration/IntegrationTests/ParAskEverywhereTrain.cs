using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.RecordedDecision;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>Asks one question before the fork, in each of two branches, and after the join.</summary>
public class ParAskEverywhereTrain : ServiceTrain<ParOrder, string>, IParAskEverywhereTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Switch<ParOrder, ParLane>(Lanes)
            .Parallel(p =>
                p.Branch("a", b => b.Switch<ParOrder, ParLane>(Lanes))
                    .Branch("b", b => b.Switch<ParOrder, ParLane>(Lanes))
            )
            .Switch<ParOrder, ParLane>(Lanes)
            .Chain<ParFinish>()
            .Resolve();

    private static Tracks<ParOrder, string, ParLane> Lanes(
        Tracks<ParOrder, string, ParLane> tracks
    ) =>
        tracks
            .When(ParLane.Express, t => t.Chain<ParTookExpress>())
            .When(ParLane.Ground, t => t.Chain<ParTookGround>());
}
