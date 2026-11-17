using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

/// <summary>A test's hold on a <c>gate</c> run: it signals once the run is inside, and waits for the test to release it.</summary>
public static class InvokedStepGate
{
    private static TaskCompletionSource _entered = New();
    private static TaskCompletionSource _release = New();

    public static Task Entered => _entered.Task;

    public static void Release() => _release.TrySetResult();

    public static Task Released => _release.Task;

    public static void Reset()
    {
        _release.TrySetResult();
        _entered = New();
        _release = New();
    }

    internal static void Enter() => _entered.TrySetResult();

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
