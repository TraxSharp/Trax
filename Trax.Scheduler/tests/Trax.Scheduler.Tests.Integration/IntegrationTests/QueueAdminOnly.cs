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

public class QueueAdminOnly(ITrainExecutionService trains) : Junction<QueuesGatedInput, Unit>
{
    public override async Task<Unit> Run(QueuesGatedInput input)
    {
        await trains
            .QueueAsync(typeof(IAdminOnlyTrain).FullName!, "{}", ct: CancellationToken.None)
            .ConfigureAwait(false);
        return Unit.Default;
    }
}
