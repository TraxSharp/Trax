using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;

namespace Trax.Scheduler.Tests.UnitTests;

public class RemoteBranchesTrain : ServiceTrain<string, Unit>, IRemoteBranchesTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<RemoteEcho>()
            .Parallel(p =>
                p.Branch("transient", b => b.Chain<RemoteBreakTransient>())
                    .Branch("permanent", b => b.Chain<RemoteBreakPermanent>())
                    .OnFailure(BranchFailurePolicy.WaitForAll)
            )
            .Chain<RemoteJoin>()
            .Resolve();
}
