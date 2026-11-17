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

/// <summary>Changes its own external id once it is running, then decides.</summary>
public class RenameThenRoute : ServiceTrain<Order, string>, IRenameThenRoute
{
    protected override Task<Either<Exception, string>> Junctions()
    {
        ExternalId = Guid.NewGuid().ToString("N");

        return Switch<Order, Fulfilment>(tracks =>
                tracks
                    .When(Fulfilment.Standard, t => t.Chain<Ship>())
                    .When(Fulfilment.ManualCheck, t => t.Chain<HoldForReview>())
            )
            .Resolve();
    }
}
