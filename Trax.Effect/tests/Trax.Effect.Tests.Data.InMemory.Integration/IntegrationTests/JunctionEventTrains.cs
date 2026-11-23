using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Decisions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.Decisions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.JunctionEvents;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

internal static class JunctionEventTrains
{
    public const string InputSecret = "INPUT-SECRET-7f3a";
    public const string OutputSecret = "OUTPUT-SECRET-91c2";
    public const string FailureSecret = "FAILURE-SECRET-5d0e";

    public static Parcel Parcel() => new($"p-{Guid.NewGuid():N}", InputSecret);

    public static IServiceCollection Register(IServiceCollection services, IDecider decider) =>
        services
            .AddSingleton(decider)
            .AddScopedTraxRoute<ILaneTrain, LaneTrain>()
            .AddScopedTraxRoute<ILaneThenFailTrain, LaneThenFailTrain>()
            .AddScopedTraxRoute<ICustomsTrain, CustomsTrain>()
            .AddScopedTraxRoute<IHeldTrain, HeldTrain>()
            .AddScopedTraxRoute<IAuditTrain, AuditTrain>()
            .AddScopedTraxRoute<ICustomsThenStampTrain, CustomsThenStampTrain>()
            .AddScopedTraxRoute<IInspectionTrain, InspectionTrain>()
            .AddScopedTraxRoute<IGroundInspectionTrain, GroundInspectionTrain>();
}
