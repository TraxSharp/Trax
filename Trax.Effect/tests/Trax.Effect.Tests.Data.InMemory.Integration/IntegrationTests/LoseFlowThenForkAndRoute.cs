using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.Decisions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.Decisions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// Builds its chain with its run's async flow suppressed, decides, then forks, and one branch
/// routes on the decision made before the fork.
/// </summary>
public class LoseFlowThenForkAndRoute : ServiceTrain<Order, string>, ILoseFlowThenForkAndRoute
{
    protected override Task<Either<Exception, string>> Junctions()
    {
        var built = LoseTheFlow.Now();

        var chain = Chain(new AfterTheChainIsBuilt(built.Task))
            .Decide<Order>(q => q.Choice<Fulfilment>())
            .Parallel(p =>
                p.Branch(
                        "routes",
                        b =>
                            b.Switch<Fulfilment>(tracks =>
                                tracks
                                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
                            )
                    )
                    .Branch("packs", b => b.Chain<PackOrder>())
            )
            .Chain<JoinRoutedAndPacked>()
            .Resolve();

        built.SetResult();
        return chain;
    }
}
