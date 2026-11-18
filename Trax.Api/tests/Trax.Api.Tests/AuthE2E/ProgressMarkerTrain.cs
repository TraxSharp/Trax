using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.TypeModules;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>Waits at <see cref="Gate"/>, then answers its note back or throws it.</summary>
[TraxMutation]
[TraxRunProgress]
[TraxAuthorize]
public class ProgressMarkerTrain
    : ServiceTrain<ProgressMarkerInput, ProgressMarkerOutput>,
        IProgressMarkerTrain
{
    internal static TaskCompletionSource Gate { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static TaskCompletionSource Waiting { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static void Reset(bool open)
    {
        Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (open)
            Gate.TrySetResult();
    }

    protected override Task<Either<Exception, ProgressMarkerOutput>> Junctions() =>
        Chain<WaitForGate>().Chain<EchoOrThrow>().Resolve();
}
