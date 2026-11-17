using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckProvider;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Minimal concrete EffectTrain for testing step effect providers.
/// </summary>
public class TestProgressTrain : ServiceTrain<string, string>
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<PassThrough1_736>().Resolve();

    private sealed class PassThrough1_736 : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input);
    }
}
