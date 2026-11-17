using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// Lets the branches of one run meet, so a run that passes ran them side by side, and lets a test
/// hold a branch until it has done something to the run.
/// </summary>
internal static class PgParSignals
{
    private static readonly ConcurrentDictionary<
        long,
        (TaskCompletionSource A, TaskCompletionSource B)
    > Meetings = new();
    private static int _waiting;
    private static TaskCompletionSource _bothWaiting = New();
    private static TaskCompletionSource _release = New();

    public static bool Met { get; private set; }

    public static Task BothWaiting => _bothWaiting.Task;

    public static void Reset()
    {
        Meetings.Clear();
        _waiting = 0;
        _bothWaiting = New();
        _release = New();
        Met = false;
    }

    public static async Task Arrive(long run, bool isA)
    {
        var (a, b) = Meetings.GetOrAdd(run, _ => (New(), New()));
        (isA ? a : b).TrySetResult();
        await (isA ? b : a).Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        Met = true;
    }

    public static void Waiting()
    {
        if (Interlocked.Increment(ref _waiting) == 2)
            _bothWaiting.TrySetResult();
    }

    public static Task Released => _release.Task;

    public static void Release() => _release.TrySetResult();

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
