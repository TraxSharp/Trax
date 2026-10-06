using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.DeadLetterRequeue;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Tests.Integration.Fakes.Services;

/// <summary>
/// What a dashboard page needs to render over the in-memory database: the data context, the
/// browser-backed settings, and the real <see cref="OperationsService"/> built the way dependency
/// injection builds it in a host (with the scope's service provider), and the real requeue-all
/// job list. <c>ITraxScheduler</c> is left to each test, which registers a recording or unused one.
/// </summary>
public static class DashboardPageServices
{
    public static IServiceCollection AddDashboardPageServices(
        this IServiceCollection services,
        InMemoryDataContextFactory data
    )
    {
        var discovery = new TrainDiscoveryService(new ServiceCollection());
        services.AddSingleton<IDataContextProviderFactory>(data);
        // Stands in for the host's database provider, so the pages' queueing actions are not
        // refused as having nothing to dispatch them (Trax.Scheduler ADR 0019).
        services.AddSingleton<ISqlDialect, StandInSqlDialect>();
        services.AddSingleton<ILocalStorageService, InMemoryLocalStorageService>();
        services.AddSingleton<IDashboardSettingsService, DashboardSettingsService>();
        services.AddSingleton<ITrainDiscoveryService>(discovery);
        services.AddSingleton<ITrustedExecutionScope, TrustedExecutionScope>();
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
        // The real requeue-all job list, as AddScheduler registers it; its fold calls the test's
        // ITraxScheduler.
        services.AddSingleton<IDeadLetterRequeueJobs>(sp => new DeadLetterRequeueJobs(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DeadLetterRequeueJobs>.Instance
        ));
        return services;
    }
}
