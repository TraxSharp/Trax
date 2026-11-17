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

public class RemoteJoin : EffectJunction<(RemoteLeft, RemoteRight), Unit>
{
    public override Task<Unit> Run((RemoteLeft, RemoteRight) input) =>
        Task.FromResult(Unit.Default);
}
