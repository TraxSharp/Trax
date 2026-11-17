using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Services.RequestHandler;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

public class ReadTrust(ITrustedExecutionScope trust) : Junction<ReadsTrustInput, Unit>
{
    public static System.Collections.Concurrent.ConcurrentDictionary<string, bool> Seen { get; } =
        new(StringComparer.Ordinal);

    public override Task<Unit> Run(ReadsTrustInput input)
    {
        Seen[input.Key] = trust.IsTrusted;
        return Task.FromResult(Unit.Default);
    }
}
