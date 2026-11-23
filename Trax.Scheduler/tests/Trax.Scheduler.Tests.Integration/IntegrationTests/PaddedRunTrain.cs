using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>Does nothing; only its input matters.</summary>
public class PaddedRunTrain : ServiceTrain<PaddedRunInput, Unit>, IPaddedRunTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Task.FromResult<Either<Exception, Unit>>(Unit.Default);
}
