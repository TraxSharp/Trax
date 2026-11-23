using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>Two branches, each asking the same question and then working; the slow one can fail.</summary>
public class ParallelProbeTrain : ServiceTrain<ParallelProbeInput, Unit>, IParallelProbeTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<ParallelProbeStart>()
            .Parallel(p =>
                p.Branch(
                        "fast",
                        b =>
                            b.Switch<ParallelProbeInput, ParallelProbeWay>(Ways)
                                .Chain<ParallelProbeFastWork>()
                                .Chain<ParallelProbeFastDone>()
                    )
                    .Branch(
                        "slow",
                        b =>
                            b.Switch<ParallelProbeInput, ParallelProbeWay>(Ways)
                                .Chain<ParallelProbeSlowWork>()
                    )
            )
            .Chain<ParallelProbeJoin>()
            .Resolve();

    private static Tracks<ParallelProbeInput, Unit, ParallelProbeWay> Ways(
        Tracks<ParallelProbeInput, Unit, ParallelProbeWay> tracks
    ) =>
        tracks
            .When(ParallelProbeWay.Left, t => t.Chain<ParallelProbeWentLeft>())
            .When(ParallelProbeWay.Right, t => t.Chain<ParallelProbeWentRight>());
}
