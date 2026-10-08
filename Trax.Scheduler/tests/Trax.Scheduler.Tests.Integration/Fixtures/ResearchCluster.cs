using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Tests.Integration.Fixtures;

/// <summary>
/// A scheduler host over a database of its own (<see cref="InvokeCluster"/>) that runs
/// <see cref="CheckpointResearchTrain"/> from manifests, with junction events, decision recording
/// and saved inputs on, and no retry backoff. Nothing runs in the background: a test runs the
/// ManifestManager, the dispatcher and each run by calling them.
/// </summary>
public sealed class ResearchCluster : IAsyncDisposable
{
    private ResearchCluster(InvokeCluster cluster, ClusterHost host)
    {
        Cluster = cluster;
        Host = host;
    }

    public InvokeCluster Cluster { get; }

    public ClusterHost Host { get; }

    public static async Task<ResearchCluster> Create(
        ClusterStore store,
        IDecider decider,
        Action<SchedulerConfigurationBuilder>? scheduling = null
    )
    {
        var cluster = await InvokeCluster.Create(store);
        var host = cluster.Host(
            machines: false,
            scheduler: true,
            configure: s => s.AddSingleton(decider),
            scheduling: s =>
            {
                s.DefaultRetryDelay(TimeSpan.Zero);
                scheduling?.Invoke(s);
            },
            data: d => d.SaveTrainParameters().AddJunctionEvents().AddDecisionRecording()
        );
        return new ResearchCluster(cluster, host);
    }

    /// <summary>A host that runs work a scheduler sends it, as a remote worker does.</summary>
    public ClusterHost Worker(IDecider decider, Action<IServiceCollection> configure) =>
        Cluster.Host(
            machines: false,
            scheduler: false,
            configure: s =>
            {
                s.AddSingleton(decider);
                configure(s);
            },
            data: d => d.SaveTrainParameters().AddJunctionEvents().AddDecisionRecording()
        );

    public async Task<Manifest> Manifest(
        string topic,
        int maxRetries = 5,
        bool replayDecisionsOnRetry = false,
        int? timeoutSeconds = null
    )
    {
        using var scope = Host.Services.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var group = await TestSetup.CreateAndSaveManifestGroup(
            data,
            name: $"group-{Guid.NewGuid():N}"
        );
        var manifest = Effect.Models.Manifest.Manifest.Create(
            new CreateManifest
            {
                Name = typeof(ICheckpointResearchTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 3600,
                MaxRetries = maxRetries,
                Properties = new ResearchInput { Topic = topic },
                ReplayDecisionsOnRetry = replayDecisionsOnRetry,
            }
        );
        manifest.ManifestGroupId = group.Id;
        manifest.TimeoutSeconds = timeoutSeconds;
        await data.Track(manifest);
        await data.SaveChanges(CancellationToken.None);
        return manifest;
    }

    /// <summary>One ManifestManager cycle, then the manifest's queued entry dispatched and run here.</summary>
    public async Task<Metadata> Cycle(Manifest manifest)
    {
        await Host.RunManifestManager();
        return await DispatchAndRun(await QueuedEntry(manifest.Id));
    }

    /// <summary>The manifest's one queued entry.</summary>
    public Task<WorkQueue> QueuedEntry(long manifestId) =>
        With(data =>
            data.WorkQueues.AsNoTracking()
                .SingleAsync(q => q.ManifestId == manifestId && q.Status == WorkQueueStatus.Queued)
        );

    /// <summary>Dispatches <paramref name="entry"/> and runs it here to its end.</summary>
    public async Task<Metadata> DispatchAndRun(WorkQueue entry)
    {
        var runId = await Dispatch(entry);
        await Host.RunJob(runId);
        return await Host.Run(runId);
    }

    /// <summary>Runs the dispatcher once and returns the run it made for <paramref name="entry"/>.</summary>
    public async Task<long> Dispatch(WorkQueue entry)
    {
        using (var scope = Host.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>().Run(Unit.Default);

        var dispatched = await Entry(entry.Id);
        return dispatched.MetadataId
            ?? throw new InvalidOperationException(
                $"Entry {entry.Id} was not dispatched: it is {dispatched.Status}."
            );
    }

    public Task<WorkQueue> Entry(long id) =>
        With(data => data.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == id));

    public Task<List<Effect.Models.Checkpoint.Checkpoint>> Checkpoints(long runId) =>
        With(data =>
            data.Checkpoints.AsNoTracking().Where(c => c.MetadataId == runId).ToListAsync()
        );

    /// <summary>
    /// The run's junction events, once the host's writer has written every step queued before
    /// the call: it writes in the background, so a read straight after a run can miss its last.
    /// </summary>
    public async Task<List<Effect.Models.JunctionRun.JunctionRun>> JunctionRuns(long runId)
    {
        // The writer's flush is internal to Trax.Effect.Data, which this assembly cannot see.
        var writer = Host.Services.GetRequiredService(
            typeof(Effect.Data.Extensions.ServiceExtensions).Assembly.GetType(
                "Trax.Effect.Data.JunctionEvents.JunctionRunWriter",
                throwOnError: true
            )!
        );
        await (
            (Task)
                writer
                    .GetType()
                    .GetMethod("FlushAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(writer, [CancellationToken.None])!
        ).WaitAsync(TimeSpan.FromSeconds(30));

        return await With(data =>
            data.JunctionRuns.AsNoTracking()
                .Where(j => j.MetadataId == runId)
                .OrderBy(j => j.Position)
                .ToListAsync()
        );
    }

    public async Task<OperationResult> Operation(
        Func<IOperationsService, Task<OperationResult>> call
    )
    {
        using var scope = Host.Services.CreateScope();
        return await call(scope.ServiceProvider.GetRequiredService<IOperationsService>());
    }

    public async Task<T> With<T>(Func<IDataContext, Task<T>> read)
    {
        using var scope = Host.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<IDataContext>());
    }

    /// <summary>
    /// Every work queue and run row whose <c>resume_from</c> names no run, which
    /// <c>TraxInvariants</c> reports as <c>resume-from-missing-run</c>.
    /// </summary>
    public async Task<List<string>> ResumesOfMissingRuns() =>
        await With(async data =>
            (
                await data
                    .WorkQueues.AsNoTracking()
                    .Where(q =>
                        q.ResumeFrom != null && !data.Metadatas.Any(m => m.Id == q.ResumeFrom)
                    )
                    .Select(q => $"work_queue {q.Id} resumes {q.ResumeFrom}")
                    .ToListAsync()
            )
                .Concat(
                    await data
                        .Metadatas.AsNoTracking()
                        .Where(r =>
                            r.ResumeFrom != null && !data.Metadatas.Any(m => m.Id == r.ResumeFrom)
                        )
                        .Select(r => $"metadata {r.Id} resumes {r.ResumeFrom}")
                        .ToListAsync()
                )
                .ToList()
        );

    public Task Reset() => Cluster.Reset();

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        await Cluster.DisposeAsync();
    }
}
