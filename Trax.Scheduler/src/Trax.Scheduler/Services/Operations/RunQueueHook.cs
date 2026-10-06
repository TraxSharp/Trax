using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.EnqueueContext;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Configuration;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Runs a train's <c>OnQueue</c> hook for a run started now, the way the mediator runs it for an
/// enqueue: on an instance resolved through the train's service interface in a scope of its own,
/// with the input handed over through <c>EnterQueueHooks</c> so <c>TrainInput</c> reads it, with a
/// metadata carrying the input, the canonical name and the run's ExternalId, with the enqueue
/// context set to the data context that writes the run's metadata row, and within
/// <c>MaxQueueHookDuration</c>. See central <c>docs/0037</c>.
/// </summary>
/// <remarks>
/// The mediator keeps its own invocation private, so this is the one place outside it that calls
/// the hook. The override check is the mediator's: only a concrete override counts, wherever it is
/// declared, and a train that keeps the no-op is never resolved.
/// </remarks>
internal static class RunQueueHook
{
    private static readonly ConcurrentDictionary<Type, MethodInfo?> OnQueueCache = new();

    /// <summary>Whether the train overrides <c>OnQueue</c>.</summary>
    public static bool Declared(TrainRegistration registration) =>
        OnQueue(registration.ImplementationType) is not null;

    /// <summary>
    /// Runs the hook when the train overrides it. Whatever the hook throws propagates unchanged,
    /// the caller deciding whether it is a refusal; a hook that runs past
    /// <c>MaxQueueHookDuration</c> throws <see cref="QueueHookTimeoutException"/>.
    /// </summary>
    public static async Task InvokeAsync(
        IServiceProvider services,
        TrainRegistration registration,
        object input,
        string externalId,
        IDataContext runContext,
        CancellationToken ct
    )
    {
        var onQueue = OnQueue(registration.ImplementationType);
        if (onQueue is null)
            return;

        var trainName = registration.ServiceType.FullName ?? registration.ServiceTypeName;
        var hookMetadata = Metadata.Create(
            new CreateMetadata
            {
                Name = trainName,
                ExternalId = externalId,
                Input = input,
            }
        );

        await using var scope = services.CreateAsyncScope();
        var train = scope.ServiceProvider.GetRequiredService(registration.ServiceType);

        using var enqueueContext = services
            .GetService<IEnqueueContextAccessor>()
            ?.Enter(runContext);

        var limit =
            services.GetService<MediatorConfiguration>()?.MaxQueueHookDuration
            ?? new MediatorConfiguration().MaxQueueHookDuration;

        if (limit == Timeout.InfiniteTimeSpan)
        {
            await InvokeHookAsync(onQueue, train, hookMetadata, ct);
            return;
        }

        using var limited = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limited.CancelAfter(limit);

        try
        {
            await InvokeHookAsync(onQueue, train, hookMetadata, limited.Token)
                .WaitAsync(limited.Token);
        }
        catch (OperationCanceledException)
            when (limited.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new QueueHookTimeoutException(trainName, limit);
        }
    }

    private static async Task InvokeHookAsync(
        MethodInfo onQueue,
        object train,
        Metadata hookMetadata,
        CancellationToken ct
    )
    {
        var enter =
            train
                .GetType()
                .GetMethod(
                    nameof(ServiceTrain<object, object>.EnterQueueHooks),
                    BindingFlags.Instance | BindingFlags.Public,
                    [typeof(Metadata)]
                )
            ?? throw new InvalidOperationException(
                $"{train.GetType().FullName} has no EnterQueueHooks(Metadata); every ServiceTrain "
                    + "does, so this train does not derive from one."
            );

        try
        {
            using var hookInput = (IDisposable)enter.Invoke(train, [hookMetadata])!;
            await (Task)onQueue.Invoke(train, [hookMetadata, ct])!;
        }
        catch (TargetInvocationException ex)
        {
            // Unwrap so the caller sees the hook's own exception, not the reflection wrapper.
            ExceptionDispatchInfo.Throw(ex.InnerException ?? ex);
        }
    }

    private static MethodInfo? OnQueue(Type implementationType) =>
        OnQueueCache.GetOrAdd(
            implementationType,
            static type =>
            {
                var method = type.GetMethod(
                    "OnQueue",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    [typeof(Metadata), typeof(CancellationToken)]
                );
                var declaringType = method?.DeclaringType;
                if (declaringType is { IsGenericType: true })
                    declaringType = declaringType.GetGenericTypeDefinition();

                return declaringType != typeof(ServiceTrain<,>) ? method : null;
            }
        );
}
