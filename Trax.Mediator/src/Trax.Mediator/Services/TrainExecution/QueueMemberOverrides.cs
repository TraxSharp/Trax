using System.Collections.Concurrent;
using System.Reflection;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Services.TrainExecution;

/// <summary>
/// Which of the queue-time members of <c>ServiceTrain&lt;,&gt;</c> a concrete train overrides. The
/// enqueue asks a train only for the members it overrides, and discovery reports the same answer
/// on <see cref="TrainDiscovery.TrainRegistration"/>, so the two cannot disagree. The reflection
/// runs once per type.
/// </summary>
internal static class QueueMemberOverrides
{
    private static readonly ConcurrentDictionary<Type, MethodInfo?> OnQueueCache = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo?> SubjectKeyCache = new();

    /// <summary>
    /// The train's overridden <c>OnQueue</c>, or null when it keeps the no-op in
    /// <c>ServiceTrain&lt;,&gt;</c>.
    /// </summary>
    public static MethodInfo? OnQueue(Type implementationType) =>
        OnQueueCache.GetOrAdd(
            implementationType,
            static type =>
                Overridden(
                    type.GetMethod(
                        "OnQueue",
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        [typeof(Metadata), typeof(CancellationToken)]
                    )
                )
        );

    /// <summary>
    /// The train's overridden <c>QueueSubjectKey</c>, or null when it keeps the base, which returns
    /// null for every train.
    /// </summary>
    public static MethodInfo? QueueSubjectKey(Type implementationType) =>
        SubjectKeyCache.GetOrAdd(
            implementationType,
            static type =>
                Overridden(
                    type.GetMethod(
                        "QueueSubjectKey",
                        BindingFlags.Instance | BindingFlags.NonPublic,
                        [typeof(Metadata)]
                    )
                )
        );

    /// <summary>Only a concrete override counts, wherever in the hierarchy it is declared.</summary>
    private static MethodInfo? Overridden(MethodInfo? method)
    {
        var declaringType = method?.DeclaringType;
        if (declaringType is { IsGenericType: true })
            declaringType = declaringType.GetGenericTypeDefinition();

        return declaringType != typeof(ServiceTrain<,>) ? method : null;
    }
}
