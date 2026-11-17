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

/// <summary>Branch a is held, then runs another junction; branch b waits to be cancelled.</summary>
public class PgParFlagTrain : ServiceTrain<PgParOrder, string>, IPgParFlagTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PgParStart>()
            .Parallel(p =>
                p.Branch("a", b => b.Chain<PgParHoldA>().Chain<PgParBumpA>())
                    .Branch("b", b => b.Chain<PgParWaitB>())
            )
            .Chain<PgParCombine>()
            .Resolve();
}
