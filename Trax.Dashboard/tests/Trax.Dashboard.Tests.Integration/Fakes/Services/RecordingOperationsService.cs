using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Tests.Integration.Fakes.Services;

/// <summary>
/// An <see cref="IOperationsService"/> that records every call in an <see cref="OperationsCallLog"/>
/// and forwards it to the real <see cref="OperationsService"/>, unless the log's
/// <see cref="OperationsCallLog.Respond"/> answers it first.
/// </summary>
public class RecordingOperationsService : DispatchProxy
{
    private IOperationsService _inner = null!;
    private OperationsCallLog _log = null!;

    /// <summary>
    /// Replaces the <see cref="IOperationsService"/> that
    /// <see cref="DashboardPageServices.AddDashboardPageServices"/> registers with the real
    /// service built the same way, recording into one log for every scope.
    /// </summary>
    public static OperationsCallLog Register(
        IServiceCollection services,
        InMemoryDataContextFactory data
    )
    {
        var log = new OperationsCallLog();
        services.AddScoped<IOperationsService>(sp =>
            Wrap(
                new OperationsService(
                    sp.GetRequiredService<ITrainDiscoveryService>(),
                    data,
                    new SchedulerConfiguration(),
                    sp.GetRequiredService<ITrainExecutionService>(),
                    sp
                ),
                log
            )
        );
        return log;
    }

    public static IOperationsService Wrap(IOperationsService inner, OperationsCallLog log)
    {
        var proxy = Create<IOperationsService, RecordingOperationsService>();
        var self = (RecordingOperationsService)(object)proxy;
        self._inner = inner;
        self._log = log;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var name = targetMethod!.Name;
        args ??= [];
        _log.Add(name, args);

        if (_log.Respond?.Invoke(name, args) is { } answer)
            return answer;

        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }
    }
}
