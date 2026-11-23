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

/// <summary>The trains the decision recording tests run, and their registrations.</summary>
internal static class DecisionTrains
{
    public static IServiceCollection Register(IServiceCollection services, IDecider decider) =>
        services
            .AddSingleton(decider)
            .AddSingleton<NonFiniteShadow>()
            .AddScopedTraxRoute<IRouteOrder, RouteOrder>()
            .AddScopedTraxRoute<IRouteThenFail, RouteThenFail>()
            .AddScopedTraxRoute<IRouteThenPeek, RouteThenPeek>()
            .AddScopedTraxRoute<IRouteTwice, RouteTwice>()
            .AddScopedTraxRoute<IRouteWithShadow, RouteWithShadow>()
            .AddScopedTraxRoute<IRenameThenRoute, RenameThenRoute>()
            .AddScopedTraxRoute<IDecideThenSwitchTwice, DecideThenSwitchTwice>()
            .AddSingleton<OddShadow>()
            .AddScopedTraxRoute<IRouteWithOddShadow, RouteWithOddShadow>()
            .AddScopedTraxRoute<IMeetThenRoute, MeetThenRoute>()
            .AddScopedTraxRoute<ILoseFlowThenRoute, LoseFlowThenRoute>()
            .AddScopedTraxRoute<IMeetThenLoseFlow, MeetThenLoseFlow>()
            .AddScopedTraxRoute<ILoseFlowThenForkAndRoute, LoseFlowThenForkAndRoute>()
            .AddScopedTraxRoute<IRunAPlainTrainOffFlow, RunAPlainTrainOffFlow>()
            .AddScopedTraxRoute<IReadThenRoute, ReadThenRoute>();
}
