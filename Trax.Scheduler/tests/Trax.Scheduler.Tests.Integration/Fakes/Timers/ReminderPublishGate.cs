using System.Text.Json.Nodes;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.Timers;

/// <summary>A test's hold on <see cref="ReminderPublish"/>.</summary>
public static class ReminderPublishGate
{
    private static TaskCompletionSource _entered = New();
    private static TaskCompletionSource _release = New();

    public static bool Armed { get; set; }

    public static Task Entered => _entered.Task;

    public static Task Released => _release.Task;

    public static void Release() => _release.TrySetResult();

    public static void Reset()
    {
        Armed = false;
        _release.TrySetResult();
        _entered = New();
        _release = New();
    }

    internal static void Enter() => _entered.TrySetResult();

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
