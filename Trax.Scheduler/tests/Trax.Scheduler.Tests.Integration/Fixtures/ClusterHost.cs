using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Data.Testing;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.Models.WorkQueue;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.ManifestManagerPollingService;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fixtures;

/// <summary>One host of an <see cref="InvokeCluster"/>.</summary>
public sealed class ClusterHost(ServiceProvider services, HeldJobs held) : IAsyncDisposable
{
    public ServiceProvider Services => services;

    internal InvokeOutcomeReconciler Reconciler =>
        services.GetRequiredService<InvokeOutcomeReconciler>();

    /// <summary>Starts a system-owned instance whose initial state invokes the step, with <paramref name="context"/>.</summary>
    public async Task<MachineInstance> Start(JsonObject context)
    {
        var scope = services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await scope
            .ServiceProvider.GetRequiredService<IMachineInstances>()
            .Start<SystemStepMachine>(
                MachineKey.Of("delivery", Guid.NewGuid().ToString("N")),
                context
            )
            .ConfigureAwait(false);
    }

    /// <summary>Saves <paramref name="user"/>'s draft in <c>Idle</c> with <paramref name="context"/> and advances it into <c>Running</c>.</summary>
    public async Task<AdvanceOutcome> EnterRunning(string user, Guid id, JsonObject context)
    {
        await StepMachine
            .Seed(services, user, id, UserStepMachine.MachineId, "Idle", context)
            .ConfigureAwait(false);
        var scope = services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await Drafts(scope).Advance(user, id, nameof(StepTrigger.Go)).ConfigureAwait(false);
    }

    public async Task<AdvanceOutcome> Advance(string user, Guid id, StepTrigger trigger)
    {
        var scope = services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await Drafts(scope).Advance(user, id, trigger.ToString()).ConfigureAwait(false);
    }

    private static ISnapshotDraftService Drafts(IServiceScope scope) =>
        scope
            .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
            .Service(UserStepMachine.MachineId)
            .NotNull();

    internal Task<IReadOnlyList<InvokeDelivery>> Sweep() =>
        Reconciler.SweepOnce(CancellationToken.None);

    internal Task<InvokeDelivery?> Deliver(string token) =>
        Reconciler.TryDeliver(token, CancellationToken.None);

    /// <summary>Runs the dispatcher once and returns the run it created for <paramref name="token"/>'s entry.</summary>
    public async Task<long> Dispatch(string token)
    {
        var scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
            await scope
                .ServiceProvider.GetRequiredService<IJobDispatcherTrain>()
                .Run(Unit.Default)
                .ConfigureAwait(false);

        var entry = await Entry(token).ConfigureAwait(false);
        return entry?.MetadataId
            ?? throw new InvalidOperationException(
                $"The entry {token} was not dispatched: it is {entry?.Status.ToString() ?? "missing"}."
            );
    }

    /// <summary>
    /// Runs the dispatched run <paramref name="metadataId"/> here, as a worker would, to its end. A run that fails
    /// or is cancelled records that on its row, which is what a test reads; the runner's rethrow is not.
    /// </summary>
    public async Task RunJob(long metadataId)
    {
        if (!held.Inputs.TryGetValue(metadataId, out var input))
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Run {metadataId} was not dispatched to this host."
                )
            );

        var scope = services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        try
        {
            await scope
                .ServiceProvider.GetRequiredService<IJobRunnerTrain>()
                .Run(new RunJobRequest(metadataId, input))
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Recorded on the run's row.
        }
    }

    /// <summary>The input this host's dispatcher handed over with the run <paramref name="metadataId"/>.</summary>
    public object? HeldInput(long metadataId) =>
        held.Inputs.TryGetValue(metadataId, out var input)
            ? input
            : throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Run {metadataId} was not dispatched to this host."
                )
            );

    /// <summary>Dispatches <paramref name="token"/>'s entry and runs it here to its end.</summary>
    public async Task<long> DispatchAndRun(string token)
    {
        var id = await Dispatch(token).ConfigureAwait(false);
        await RunJob(id).ConfigureAwait(false);
        return id;
    }

    /// <summary>
    /// One manifest manager cycle through the polling service: the timeouts, the reapers, and their lifecycle
    /// events once it commits.
    /// </summary>
    public async Task RunManifestManager()
    {
        var polling = new ManifestManagerPollingService(
            services,
            services.GetRequiredService<SchedulerConfiguration>(),
            NullLogger<ManifestManagerPollingService>.Instance,
            services.GetRequiredService<ISqlDialect>()
        );
        await polling.RunManifestManager(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<SnapshotDraft?> Row(Guid id, string machine)
    {
        var scope = services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.Machine == machine)
            .ConfigureAwait(false);
    }

    /// <summary>Every owner's row of instance <paramref name="id"/> of <paramref name="machine"/>.</summary>
    public async Task<IReadOnlyList<SnapshotDraft>> Rows(Guid id, string machine)
    {
        var scope = services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.AsNoTracking()
            .Where(x => x.Id == id && x.Machine == machine)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<WorkQueue?> Entry(string token)
    {
        var scope = services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .WorkQueues.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ExternalId == token)
            .ConfigureAwait(false);
    }

    /// <summary>Every entry an instance's invoking states queued, oldest first.</summary>
    public async Task<IReadOnlyList<WorkQueue>> Entries(Guid instance)
    {
        var scope = services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .WorkQueues.AsNoTracking()
            .Where(x => x.InvokingInstanceId == instance)
            .OrderBy(x => x.Id)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<Metadata> Run(long metadataId)
    {
        var scope = services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .Metadatas.AsNoTracking()
            .SingleAsync(x => x.Id == metadataId)
            .ConfigureAwait(false);
    }

    /// <summary>Moves a run's start back by <paramref name="age"/>, as if it had been going that long.</summary>
    public async Task Age(long metadataId, TimeSpan age)
    {
        var scope = services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var start = TimeProvider.System.GetUtcNow().UtcDateTime - age;
        await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .Metadatas.Where(x => x.Id == metadataId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.StartTime, start))
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => services.DisposeAsync();
}
