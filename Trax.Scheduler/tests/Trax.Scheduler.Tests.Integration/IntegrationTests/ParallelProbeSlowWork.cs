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

public class ParallelProbeSlowWork : EffectJunction<ParallelProbeInput, ParallelProbeSlow>
{
    public override async Task<ParallelProbeSlow> Run(ParallelProbeInput input)
    {
        if (ParallelProbe.Hold)
        {
            // Stopped by its run's cancellation or by the fast branch's.
            var stopped = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            await using (
                CancellationToken
                    .Register(() => stopped.TrySetCanceled(CancellationToken.None))
                    .ConfigureAwait(false)
            )
                await stopped.Task.ConfigureAwait(false);
        }

        if (ParallelProbe.Fail)
        {
            await ParallelProbe
                .FastBranchFinishedIn(Metadata!.TrainMetadataId)
                .ConfigureAwait(false);
            throw new TimeoutException("the slow branch timed out");
        }

        return new ParallelProbeSlow(input.Value);
    }
}
