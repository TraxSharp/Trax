using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.DormantDependentContext;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Train carrying [TraxRemote] so the scheduler builder's attribute-discovery loop has a hit.
/// </summary>
/// <remarks>
/// Abstract so the assembly scan the other tests run does not register it: a <c>[TraxRemote]</c>
/// train with no routed submitter fails the scheduler's build, and most of those tests configure
/// none. A test that wants it registers it itself; discovery reads only its registration, and
/// nothing here resolves it.
/// </remarks>
[TraxRemote]
internal abstract class RemoteCoverageTrain
    : ServiceTrain<RemoteCoverageInput, Unit>,
        IRemoteCoverageTrain
{
    protected override async Task<Either<Exception, Unit>> Junctions() => Resolve();
}
