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

public class ParWaitTrain : ServiceTrain<ParOrder, string>, IParWaitTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<ParStart>()
            .Parallel(p =>
                p.Branch("a", b => b.Chain<ParWaitA>().Chain<ParRefineA>())
                    .Branch("b", b => b.Chain<ParWaitB>().Chain<ParRefineB>())
            )
            .Chain<ParCombine>()
            .Resolve();
}
