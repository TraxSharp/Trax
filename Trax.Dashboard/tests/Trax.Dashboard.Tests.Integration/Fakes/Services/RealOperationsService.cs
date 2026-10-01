using Microsoft.Extensions.DependencyInjection;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Effect.Data.Services.EnqueueContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Tests.Integration.Fakes.Services;

/// <summary>
/// Registers the scheduler's real <see cref="OperationsService"/>, through the constructor
/// dependency injection uses, over the mediator's real <see cref="TrainExecutionService"/> and an
/// in-memory store. The service resolves its job submitter, trusted scope and train instances
/// from the same provider the dialog renders with, as it does from a circuit's scope in a host.
/// </summary>
public static class RealOperationsService
{
    public static IServiceCollection AddRealOperationsService(
        this IServiceCollection services,
        ITrainDiscoveryService discovery,
        InMemoryDataContextFactory data
    )
    {
        services.AddSingleton<IDataContextProviderFactory>(data);
        services.AddSingleton(discovery);
        services.AddSingleton<ITrustedExecutionScope, TrustedExecutionScope>();
        // What AddMediator registers for a train's OnQueue hook to enqueue on.
        services.AddSingleton<IEnqueueContextAccessor, EnqueueContextAccessor>();
        services.AddScoped<ITrainExecutionService>(sp => new TrainExecutionService(
            discovery,
            runExecutor: null!,
            concurrencyLimiter: null!,
            data,
            new MediatorConfiguration(),
            sp
        ));
        services.AddScoped<IOperationsService>(sp => new OperationsService(
            discovery,
            data,
            new SchedulerConfiguration(),
            sp.GetRequiredService<ITrainExecutionService>(),
            sp
        ));
        return services;
    }
}
