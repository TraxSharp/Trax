using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>What the Parallel probe's runs did, shared because the scheduler builds the junctions.</summary>
public static class ParallelProbe
{
    private static TaskCompletionSource _held = New();
    private static TaskCompletionSource _released = New();

    public static ParallelProbeDecider Decider { get; } = new();

    /// <summary>
    /// Whether the slow branch fails its next run. It fails only once the fast branch of the same run
    /// has finished, so a failed run has asked and taken a track in both branches, whatever order
    /// the thread pool starts them in: a failure cancels the branches still running.
    /// </summary>
    public static bool Fail { get; set; }

    // Completed by each run's fast branch as it finishes, keyed by the run.
    private static readonly ConcurrentDictionary<long, TaskCompletionSource> FastFinished = new();

    /// <summary>Whether the fast branch holds its next run until <see cref="Release"/>.</summary>
    public static bool Hold { get; set; }

    public static Task Held => _held.Task;

    public static ConcurrentQueue<(long Run, string Branch, string Way)> Taken { get; } = new();

    public static void Reset()
    {
        Fail = false;
        Hold = false;
        _held = New();
        _released = New();
        Taken.Clear();
        FastFinished.Clear();
        Decider.Reset();
    }

    public static void Release() => _released.TrySetResult();

    public static void FastBranchFinished(long run) => FastFinishedIn(run).TrySetResult();

    public static Task FastBranchFinishedIn(long run) =>
        FastFinishedIn(run).Task.WaitAsync(TimeSpan.FromSeconds(30));

    private static TaskCompletionSource FastFinishedIn(long run) =>
        FastFinished.GetOrAdd(run, _ => New());

    public static async Task HoldIfAsked()
    {
        if (!Hold)
            return;

        _held.TrySetResult();
        await _released.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
    }

    public static List<(string Branch, string Way)> TakenBy(long run) =>
        Taken.Where(t => t.Run == run).Select(t => (t.Branch, t.Way)).ToList();

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
