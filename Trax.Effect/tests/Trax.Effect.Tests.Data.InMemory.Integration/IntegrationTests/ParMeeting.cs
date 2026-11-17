using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.RecordedDecision;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// Makes two branches meet: each waits for the other to arrive, so a run that passes ran them side
/// by side. Also says when both waiting junctions are waiting, and when a branch has inspected.
/// </summary>
internal static class ParMeeting
{
    private static TaskCompletionSource _a = New();
    private static TaskCompletionSource _b = New();
    private static TaskCompletionSource _inspected = New();
    private static int _waiting;
    private static TaskCompletionSource _bothWaiting = New();

    public static bool Met { get; private set; }

    public static Task BothWaiting => _bothWaiting.Task;

    public static Task Inspected => _inspected.Task;

    public static void Reset()
    {
        (_a, _b, _inspected, _bothWaiting) = (New(), New(), New(), New());
        _waiting = 0;
        Met = false;
    }

    public static async Task Arrive(bool isA)
    {
        (isA ? _a : _b).TrySetResult();
        await (isA ? _b : _a)
            .Task.WaitAsync(TimeSpan.FromSeconds(30), TimeProvider.System)
            .ConfigureAwait(false);
        Met = true;
    }

    public static void Waiting()
    {
        if (Interlocked.Increment(ref _waiting) == 2)
            _bothWaiting.TrySetResult();
    }

    public static void InspectionDone() => _inspected.TrySetResult();

    /// <summary>Completes only when <paramref name="token"/> is cancelled, by throwing.</summary>
    public static async Task UntilCancelled(CancellationToken token)
    {
        var cancelled = New();
        await using ((token.Register(() => cancelled.TrySetCanceled(token))).ConfigureAwait(false))
            await cancelled.Task.ConfigureAwait(false);
    }

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
