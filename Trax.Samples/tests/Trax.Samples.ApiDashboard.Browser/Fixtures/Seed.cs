using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Core.Functional;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.PersistedOperation;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Samples.ApiDashboard.Browser.Factories;
using Trax.Samples.ApiDashboard.Browser.Host;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Samples.ApiDashboard.Browser.Fixtures;

/// <summary>
/// Builds the states a console page shows, one builder per state, through the host's own services (the scheduler,
/// the mediator, the machine instances, the operations service, the persisted-operation store), never SQL. Each
/// test seeds only what it needs, under names of its own, and <see cref="DisposeAsync"/> undoes what a builder
/// switched on the host (an outage, a hold, a paused dispatcher, a stopped machine sweep), newest first, so the
/// next test starts on a healthy host. Every wait is on the host's state (Trax.Docs/adr/0006).
/// </summary>
public sealed class Seed(ConsoleHost host) : IAsyncDisposable
{
    /// <summary>A cron that names a moment a year away: the manifest runs only when triggered.</summary>
    private static readonly Schedule OnlyWhenTriggered = Schedule.FromCron("0 0 1 1 *");

    private readonly Stack<Func<Task>> _undo = new();

    private bool _sweepStopped;

    private Outages Outages => host.Services.GetRequiredService<Outages>();

    private Holds Holds => host.Services.GetRequiredService<Holds>();

    /// <summary>A key no other seed uses, readable in a row.</summary>
    public static string NewKey(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];

    /// <summary>
    /// A manifest whose run failed once with <c>MaxRetries(0)</c>, so the scheduler dead-lettered it. Its supplier
    /// is back by the time this returns, so a requeue of it succeeds.
    /// </summary>
    public async Task<DeadLetter> DeadLetterAsync(string? group = null)
    {
        var key = NewKey("feed");
        Outages.Down(key);
        try
        {
            var manifest = await ScheduleAsync<IImportFeedTrain>(
                key,
                o =>
                {
                    o.MaxRetries(0);
                    if (group is not null)
                        o.Group(group);
                }
            );
            await InScope<ITraxScheduler>(s => s.TriggerAsync(key));
            return await Wait.ForAsync(
                () =>
                    host.QueryAsync(dc =>
                        dc.DeadLetters.AsNoTracking()
                            .FirstOrDefaultAsync(d =>
                                d.ManifestId == manifest.Id
                                && d.Status == DeadLetterStatus.AwaitingIntervention
                            )
                    ),
                BrowserTimeouts.Server,
                $"the scheduler dead-letters {key}"
            );
        }
        finally
        {
            Outages.Up(key);
        }
    }

    /// <summary>Two dead letters of two manifests that failed the same way: their reasons are the same text.</summary>
    public async Task<(DeadLetter First, DeadLetter Second)> DeadLettersSharingAReasonAsync()
    {
        var first = await DeadLetterAsync();
        var second = await DeadLetterAsync();
        second.Reason.Should().Be(first.Reason, "both failed once against no retries");
        return (first, second);
    }

    /// <summary>
    /// A run of <see cref="ISettleClaimTrain"/> that answered its Gate, stored its checkpoint and then failed at
    /// <see cref="NotifyClaimant"/>: what a resume starts from. Its failed step is recorded by the time this returns.
    /// </summary>
    public async Task<Metadata> FailedRunWithCheckpointAsync()
    {
        var key = NewKey("claim");
        Outages.Down(key);
        try
        {
            var run = await QueueAndWaitAsync<ISettleClaimTrain>(key, TrainState.Failed);
            await Wait.UntilAsync(
                () => host.QueryAsync(dc => dc.Checkpoints.AnyAsync(c => c.MetadataId == run.Id)),
                BrowserTimeouts.Server,
                $"run {run.Id} stores its checkpoint"
            );
            // Steps are stored by a background writer, after the run has ended.
            await Wait.UntilAsync(
                () =>
                    host.QueryAsync(dc =>
                        dc.JunctionRuns.AnyAsync(j =>
                            j.MetadataId == run.Id
                            && j.Name == nameof(NotifyClaimant)
                            && j.State == JunctionRunState.Failed
                        )
                    ),
                BrowserTimeouts.Server,
                $"run {run.Id} records its failed step"
            );
            return run;
        }
        finally
        {
            Outages.Up(key);
        }
    }

    /// <summary>A completed run of <see cref="ISettleClaimTrain"/>, whose Gate a decider answered.</summary>
    public Task<Metadata> GatedRunAsync() =>
        QueueAndWaitAsync<ISettleClaimTrain>(NewKey("claim"), TrainState.Completed);

    /// <summary>A completed run that <see cref="RelayMachine"/>'s <c>Relaying</c> state invoked.</summary>
    public async Task<(MachineInstance Instance, Metadata Run)> RunInvokedByMachineAsync()
    {
        var key = NewKey("parcel");
        var instance = await StartRelayAsync(key);
        var run = await Wait.ForAsync(
            () =>
                host.QueryAsync(dc =>
                    dc.Metadatas.AsNoTracking()
                        .FirstOrDefaultAsync(m =>
                            m.InvokingInstanceId == instance.Id
                            && m.TrainState == TrainState.Completed
                        )
                ),
            BrowserTimeouts.Server,
            $"{RelayMachine.Id} {instance.Id} invokes its run and the run completes"
        );
        return (instance, run);
    }

    /// <summary>
    /// A queue entry of <see cref="ISyncMailboxTrain"/> left staged: its <c>OnQueue</c> hook waits until this seed
    /// is disposed, so the entry is written but not yet confirmed.
    /// </summary>
    public async Task<WorkQueue> StagedQueueEntryAsync()
    {
        var key = NewKey("mailbox");
        Holds.Hold(key);
        var after = await host.QueryAsync(dc => dc.WorkQueues.MaxAsync(w => (long?)w.Id)) ?? 0;
        var queued = InScope<ITrainExecutionService, QueueTrainResult>(q =>
            q.QueueAsync(TrainName<ISyncMailboxTrain>(), Input(key))
        );
        _undo.Push(async () =>
        {
            Holds.Release(key);
            await queued.WaitAsync(BrowserTimeouts.Server);
        });
        return await Wait.ForAsync(
            () =>
                host.QueryAsync(dc =>
                    dc.WorkQueues.AsNoTracking()
                        .FirstOrDefaultAsync(w =>
                            w.Id > after
                            && w.TrainName == TrainName<ISyncMailboxTrain>()
                            && w.ConfirmedAt == null
                            && w.Status == WorkQueueStatus.Queued
                        )
                ),
            BrowserTimeouts.Server,
            $"the staged entry for {key} is written"
        );
    }

    /// <summary>
    /// A queued entry of <see cref="IReconcileAccountTrain"/> blocked behind another run about the same subject,
    /// which holds until this seed is disposed. Returns the blocked entry and the run it waits behind.
    /// </summary>
    public async Task<(WorkQueue Blocked, Metadata Holding)> BlockedQueueEntryAsync()
    {
        var key = NewKey("account");
        Holds.Hold(key);
        _undo.Push(() =>
        {
            Holds.Release(key);
            return Task.CompletedTask;
        });
        var holding = await QueueAndWaitAsync<IReconcileAccountTrain>(key, TrainState.InProgress);
        var second = await InScope<ITrainExecutionService, QueueTrainResult>(q =>
            q.QueueAsync(TrainName<IReconcileAccountTrain>(), Input(key))
        );
        var blocked = await host.QueryAsync(dc =>
            dc.WorkQueues.AsNoTracking().SingleAsync(w => w.Id == second.WorkQueueId)
        );
        blocked.Status.Should().Be(WorkQueueStatus.Queued);
        return (blocked, holding);
    }

    /// <summary>
    /// <paramref name="count"/> runs of <see cref="IReconcileAccountTrain"/> in progress, each about a key of its own
    /// and held until this seed is disposed, so a cancel requested of them stays visible as requested.
    /// </summary>
    public async Task<IReadOnlyList<Metadata>> HeldRunsAsync(int count)
    {
        var runs = new List<Metadata>();
        for (var i = 0; i < count; i++)
        {
            var key = NewKey("ledger");
            Holds.Hold(key);
            _undo.Push(() =>
            {
                Holds.Release(key);
                return Task.CompletedTask;
            });
            runs.Add(await QueueAndWaitAsync<IReconcileAccountTrain>(key, TrainState.InProgress));
        }
        return runs;
    }

    /// <summary>
    /// A run of <see cref="IReconcileAccountTrain"/> in progress and held, with what releases it; disposing the seed
    /// releases it too.
    /// </summary>
    public async Task<(Metadata Run, Action Release)> HeldRunAsync()
    {
        var key = NewKey("ledger");
        Holds.Hold(key);
        _undo.Push(() =>
        {
            Holds.Release(key);
            return Task.CompletedTask;
        });
        var run = await QueueAndWaitAsync<IReconcileAccountTrain>(key, TrainState.InProgress);
        return (run, () => Holds.Release(key));
    }

    /// <summary>
    /// A manifest that runs only when triggered, in <paramref name="group"/> when one is named: what Queue now,
    /// Queue later, Edit and the on/off switches act on.
    /// </summary>
    public Task<Manifest> OnDemandManifestAsync(string? group = null) =>
        ScheduleAsync<IImportFeedTrain>(
            NewKey("feed"),
            o =>
            {
                if (group is not null)
                    o.Group(group);
            }
        );

    /// <summary>
    /// A manifest of <see cref="ISettleClaimTrain"/>, whose Gate a decider answers, so the console offers its
    /// replay-on-retry switch. It runs only when triggered.
    /// </summary>
    public Task<Manifest> DecidingManifestAsync() =>
        ScheduleAsync<ISettleClaimTrain>(NewKey("claim"));

    /// <summary>
    /// A manifest of <see cref="IReconcileAccountTrain"/>, in <paramref name="group"/> when one is named, triggered
    /// once and its run held in progress until this seed is disposed: what Cancel runs and Cancel group act on.
    /// </summary>
    public async Task<(Manifest Manifest, Metadata Run)> ManifestWithAHeldRunAsync(
        string? group = null
    )
    {
        var key = NewKey("account");
        Holds.Hold(key);
        _undo.Push(() =>
        {
            Holds.Release(key);
            return Task.CompletedTask;
        });
        var manifest = await ScheduleAsync<IReconcileAccountTrain>(
            key,
            o =>
            {
                if (group is not null)
                    o.Group(group);
            }
        );
        await InScope<ITraxScheduler>(s => s.TriggerAsync(key));
        var run = await Wait.ForAsync(
            () =>
                host.QueryAsync(dc =>
                    dc.Metadatas.AsNoTracking()
                        .FirstOrDefaultAsync(m =>
                            m.ManifestId == manifest.Id && m.TrainState == TrainState.InProgress
                        )
                ),
            BrowserTimeouts.Server,
            $"the triggered run of {key} is in progress"
        );
        return (manifest, run);
    }

    /// <summary>A group of its own holding one manifest that runs only when triggered. Returns both.</summary>
    public async Task<(ManifestGroup Group, Manifest Manifest)> GroupOfOneAsync()
    {
        var name = NewKey("group");
        var manifest = await OnDemandManifestAsync(name);
        return (await GroupNamedAsync(name), manifest);
    }

    /// <summary>The group the host stores under <paramref name="name"/>.</summary>
    public Task<ManifestGroup> GroupNamedAsync(string name) =>
        host.QueryAsync(dc => dc.ManifestGroups.AsNoTracking().SingleAsync(g => g.Name == name));

    /// <summary>
    /// A <see cref="RelayMachine"/> instance in <c>Relaying</c>, the run it invoked in progress and held until
    /// this seed is disposed: an instance whose state waits on a live run, which an operator may cancel.
    /// </summary>
    public async Task<(MachineInstance Instance, Metadata Run)> InstanceWaitingOnAHeldRunAsync()
    {
        var key = NewKey("parcel");
        Holds.Hold(key);
        _undo.Push(() =>
        {
            Holds.Release(key);
            return Task.CompletedTask;
        });
        var instance = await StartRelayAsync(key);
        var run = await Wait.ForAsync(
            () =>
                host.QueryAsync(dc =>
                    dc.Metadatas.AsNoTracking()
                        .FirstOrDefaultAsync(m =>
                            m.InvokingInstanceId == instance.Id
                            && m.TrainState == TrainState.InProgress
                        )
                ),
            BrowserTimeouts.Server,
            $"{RelayMachine.Id} {instance.Id}'s run is in progress"
        );
        return (instance, run);
    }

    /// <summary>
    /// A <see cref="ReminderMachine"/> instance whose timer is past due and has not fired: the machine sweep that
    /// fires timers is stopped until this seed is disposed, which then fires it.
    /// </summary>
    public async Task<MachineInstance> OverdueTimerAsync()
    {
        await StopMachineSweepAsync();
        var instance = await InScope<IMachineInstances, MachineInstance>(m =>
            m.Start<ReminderMachine>(MachineKey.Of(NewKey("reminder")))
        );
        await Wait.UntilAsync(
            () =>
                host.QueryAsync(async dc =>
                    await dc
                        .SnapshotDrafts.AsNoTracking()
                        .Where(d => d.Id == instance.Id)
                        .Select(d => d.DueAt)
                        .SingleAsync()
                        is { } due
                    && due.AddSeconds(2) < DateTimeOffset.UtcNow
                ),
            BrowserTimeouts.Server,
            $"{ReminderMachine.Id} {instance.Id}'s timer is past due"
        );
        return instance;
    }

    /// <summary>
    /// A <see cref="RelayMachine"/> instance still in <c>Relaying</c> although the run it invoked ended: the run's
    /// queue entry was cancelled before dispatch, an outcome only the machine sweep applies, and the sweep is
    /// stopped (and the dispatcher paused) until this seed is disposed. Returns the instance and the cancelled entry.
    /// </summary>
    public async Task<(
        MachineInstance Instance,
        WorkQueue Entry
    )> InstanceWaitingOnAnEndedRunAsync()
    {
        await StopMachineSweepAsync();
        await PausedDispatcherAsync();
        var instance = await StartRelayAsync(NewKey("parcel"));
        var entry = await Wait.ForAsync(
            () =>
                host.QueryAsync(dc =>
                    dc.WorkQueues.AsNoTracking()
                        .FirstOrDefaultAsync(w => w.InvokingInstanceId == instance.Id)
                ),
            BrowserTimeouts.Server,
            $"{RelayMachine.Id} {instance.Id} queues its run"
        );
        var cancelled = await InScope<IOperationsService, OperationResult>(o =>
            o.CancelWorkQueueEntryAsync(entry.Id, CancellationToken.None)
        );
        cancelled.Success.Should().BeTrue(cancelled.Message);
        return (
            instance,
            await host.QueryAsync(dc =>
                dc.WorkQueues.AsNoTracking().SingleAsync(w => w.Id == entry.Id)
            )
        );
    }

    /// <summary>A <see cref="GateMachine"/> instance of its own, closed; <see cref="OpenGateAsync"/> opens it.</summary>
    public async Task<(MachineInstance Instance, MachineKey Key)> ClosedGateAsync()
    {
        var key = MachineKey.Of(NewKey("lane"));
        var instance = await InScope<IMachineInstances, MachineInstance>(m =>
            m.Start<GateMachine>(key)
        );
        return (instance, key);
    }

    /// <summary>Opens the gate <paramref name="key"/> names, as the application that owns it would.</summary>
    public Task OpenGateAsync(MachineKey key) =>
        InScope<IMachineInstances, AdvanceOutcome>(m =>
            m.Advance<GateMachine>(key, nameof(GateTrigger.Open))
        );

    /// <summary>A manifest the operator disabled: it stays listed and does not run.</summary>
    public async Task<Manifest> DisabledManifestAsync()
    {
        var key = NewKey("disabled");
        var manifest = await ScheduleAsync<IImportFeedTrain>(key);
        await InScope<ITraxScheduler>(s => s.DisableAsync(key));
        return await host.QueryAsync(dc =>
            dc.Manifests.AsNoTracking().SingleAsync(m => m.Id == manifest.Id)
        );
    }

    /// <summary>
    /// A manifest that runs every night at 02:00 UTC, so its schedule reads in words and it has a next run.
    /// </summary>
    public Task<Manifest> NightlyManifestAsync() =>
        ScheduleAsync<IImportFeedTrain>(
            NewKey("nightly"),
            schedule: Schedule.FromCron("0 2 * * *")
        );

    /// <summary>A manifest in a group of its own that is switched off. Returns the group's name.</summary>
    public async Task<string> DisabledGroupAsync()
    {
        var group = NewKey("off");
        await ScheduleAsync<IImportFeedTrain>(
            NewKey("feed"),
            o => o.Group(group, g => g.Enabled(false))
        );
        return group;
    }

    /// <summary>
    /// A group of its own whose one manifest's run failed and was dead-lettered, so its latest ended run
    /// failed. Returns the group's name.
    /// </summary>
    public async Task<string> FailingGroupAsync()
    {
        var group = NewKey("failing");
        await DeadLetterAsync(group);
        return group;
    }

    /// <summary>The job dispatcher switched off, as an operator does from the settings; on again on dispose.</summary>
    public async Task PausedDispatcherAsync()
    {
        await SetDispatcherAsync(enabled: false);
        _undo.Push(() => SetDispatcherAsync(enabled: true));
    }

    /// <summary>An active persisted operation the store holds, as a deploy step would upload it.</summary>
    public Task<PersistedOperation> PersistedOperationAsync()
    {
        var name = $"browserSeed{Guid.NewGuid():N}"[..19];
        return InScope<IPersistedOperationStore, PersistedOperation>(store =>
            store.UpsertAsync(
                $"{name}.v1",
                $"query {name} {{ operations {{ hosts {{ name }} }} }}",
                null,
                CancellationToken.None
            )
        );
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        while (_undo.TryPop(out var undo))
        {
            try
            {
                await undo();
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }
        if (failures.Count > 0)
            throw new AggregateException("Undoing a seed failed.", failures);
    }

    // Stops the sweep once per seed, however many builders ask, and starts it once on dispose. The sweep is a
    // BackgroundService: each StartAsync starts a loop of its own and StopAsync stops only the newest, so a second
    // start would leave a loop running that no later stop reaches, firing the timers another test needs left due.
    private async Task StopMachineSweepAsync()
    {
        if (_sweepStopped)
            return;
        _sweepStopped = true;
        await host.MachineSweep.StopAsync(CancellationToken.None);
        _undo.Push(() => host.MachineSweep.StartAsync(CancellationToken.None));
    }

    private Task SetDispatcherAsync(bool enabled) =>
        InScope<IOperationsService, OperationResult>(o =>
                o.UpdateSchedulerConfigAsync(
                    new UpdateSchedulerConfigInput(JobDispatcherEnabled: enabled),
                    CancellationToken.None
                )
            )
            .ContinueWith(t => t.Result.Success.Should().BeTrue(t.Result.Message));

    private async Task<MachineInstance> StartRelayAsync(string key)
    {
        var machineKey = MachineKey.Of(key);
        var instance = await InScope<IMachineInstances, MachineInstance>(m =>
            m.Start<RelayMachine>(machineKey, new JsonObject { ["key"] = key })
        );
        await InScope<IMachineInstances, AdvanceOutcome>(m =>
            m.Advance<RelayMachine>(machineKey, nameof(RelayTrigger.Send))
        );
        return instance;
    }

    private Task<Manifest> ScheduleAsync<TTrain>(
        string key,
        Action<ScheduleOptions>? options = null,
        Schedule? schedule = null
    )
        where TTrain : Trax.Effect.Services.ServiceTrain.IServiceTrain<KeyedInput, Unit> =>
        InScope<ITraxScheduler, Manifest>(s =>
            s.ScheduleAsync<TTrain, KeyedInput, Unit>(
                key,
                new KeyedInput { Key = key },
                schedule ?? OnlyWhenTriggered,
                options
            )
        );

    // Queues the train about key through the mediator and waits for its run to reach state.
    private async Task<Metadata> QueueAndWaitAsync<TTrain>(string key, TrainState state)
    {
        var queued = await InScope<ITrainExecutionService, QueueTrainResult>(q =>
            q.QueueAsync(TrainName<TTrain>(), Input(key))
        );
        return await Wait.ForAsync(
            () =>
                host.QueryAsync(dc =>
                    dc.Metadatas.AsNoTracking()
                        .FirstOrDefaultAsync(m =>
                            m.ExternalId == queued.ExternalId && m.TrainState == state
                        )
                ),
            BrowserTimeouts.Server,
            $"the run of {TrainName<TTrain>()} about {key} reaches {state}"
        );
    }

    private static string TrainName<TTrain>() => typeof(TTrain).FullName!;

    private static string Input(string key) =>
        JsonSerializer.Serialize(new KeyedInput { Key = key });

    private async Task InScope<TService>(Func<TService, Task> act)
        where TService : notnull
    {
        await using var scope = host.Services.CreateAsyncScope();
        using var trusted = scope
            .ServiceProvider.GetRequiredService<ITrustedExecutionScope>()
            .BeginTrusted("browser test seed");
        await act(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<T> InScope<TService, T>(Func<TService, Task<T>> act)
        where TService : notnull
    {
        await using var scope = host.Services.CreateAsyncScope();
        using var trusted = scope
            .ServiceProvider.GetRequiredService<ITrustedExecutionScope>()
            .BeginTrusted("browser test seed");
        return await act(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
