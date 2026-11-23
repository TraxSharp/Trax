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

/// <summary>Holds a run at its first junction until the test releases it.</summary>
internal static class MarkerGates
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource> Gates = new(
        StringComparer.Ordinal
    );

    public static TaskCompletionSource Hold(string key) =>
        Gates.GetOrAdd(key, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));

    public static Task Wait(string key) =>
        Gates.TryGetValue(key, out var gate) ? gate.Task : Task.CompletedTask;
}
