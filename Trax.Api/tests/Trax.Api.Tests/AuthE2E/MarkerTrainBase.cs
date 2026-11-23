using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Auth.ApiKey;
using Trax.Api.DTOs;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.Runs;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.ChainVerification;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests.AuthE2E;

public abstract class MarkerTrainBase : ServiceTrain<MarkerInput, string>
{
    // Carried by the train rather than registered, so the other hosts that scan this assembly do
    // not need a decider of their own.
    private static readonly IDecider Decider = new ScriptedDecider()
        .Choose(MarkerLane.Fast, 0.8)
        .Choose(MarkerTier.Red, 0.9);

    protected override Task<Either<Exception, string>> Junctions() =>
        AddServices(Decider)
            .Chain<HoldForRelease>()
            .Switch<MarkerInput, MarkerLane>(tracks =>
                tracks
                    .When(MarkerLane.Fast, t => t.Chain<PassLane>())
                    .When(MarkerLane.Slow, t => t.Chain<PassLane>())
            )
            .Switch<MarkerInput, MarkerTier>(tracks =>
                tracks
                    .When(MarkerTier.Green, t => t.Chain<RevealSecret>())
                    .When(MarkerTier.Red, t => t.Chain<RevealSecret>())
            )
            .Chain<FinishOrExplode>()
            .Resolve();
}
