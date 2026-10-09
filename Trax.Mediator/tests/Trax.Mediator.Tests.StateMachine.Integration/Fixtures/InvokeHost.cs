using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Tests.StateMachine.Integration.Fakes;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fixtures;

/// <summary>The providers a host can store drafts on.</summary>
public enum StoreProvider
{
    Postgres,
    Sqlite,
    InMemory,
}

/// <summary>What a test host registers beyond the provider.</summary>
public sealed record HostOptions
{
    public bool Mediator { get; init; } = true;

    public bool JunctionProgress { get; init; } = true;

    /// <summary>Registrations made after Trax's, so the last one wins.</summary>
    public Action<IServiceCollection>? Configure { get; init; }
}

/// <summary>
/// A host with <c>AddStateMachines</c> and <c>AddMediator</c> over this assembly's machines and trains, on Postgres
/// (the shared throwaway database) or a fresh SQLite file, with the test principal and a recording authorization.
/// </summary>
public sealed class InvokeHost : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly string? _sqliteFile;

    private InvokeHost(ServiceProvider provider, string? sqliteFile)
    {
        _provider = provider;
        _sqliteFile = sqliteFile;
    }

    public IServiceProvider Services => _provider;

    public static InvokeHost Create(StoreProvider provider, HostOptions? options = null)
    {
        options ??= new HostOptions();
        var file =
            provider == StoreProvider.Sqlite
                ? Path.Combine(Path.GetTempPath(), $"sm_invoke_{Guid.NewGuid():N}.db")
                : null;
        var assembly = typeof(GoodMachine).Assembly;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IPlainContract, PlainContractStep>();
        services.AddTrax(trax =>
        {
            var effects = trax.AddEffects(e =>
            {
                var data = provider switch
                {
                    StoreProvider.Postgres => e.UsePostgres(PostgresSetup.ConnectionString),
                    StoreProvider.Sqlite => e.UseSqlite($"Data Source={file}"),
                    _ => e.UseInMemory(),
                };
                return options.JunctionProgress ? data.AddJunctionProgress() : data;
            });
            var machines = effects.AddStateMachines(assembly);
            if (options.Mediator)
                machines.AddMediator(assembly);
        });
        services.AddSingleton<ISnapshotPrincipal, TestPrincipal>();
        services.AddScoped<ITrainAuthorizationService, RecordingAuthorization>();
        options.Configure?.Invoke(services);

        return new InvokeHost(services.BuildServiceProvider(), file);
    }

    public IServiceScope Scope() => _provider.CreateScope();

    public ISnapshotDraftService Service(IServiceScope scope, string machine) =>
        scope.ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>().Service(machine)!;

    public async Task<MachineInstance> Start<TMachine>(MachineKey key)
        where TMachine : IMachine
    {
        using var scope = Scope();
        return await scope
            .ServiceProvider.GetRequiredService<IMachineInstances>()
            .Start<TMachine>(key);
    }

    /// <summary>What the startup check reports for <typeparamref name="TMachine"/> on this host.</summary>
    public IReadOnlyList<string> Problems<TMachine>()
        where TMachine : IMachine
    {
        using var scope = Scope();
        var machine = scope.ServiceProvider.GetServices<IMachine>().OfType<TMachine>().Single();
        return InvokesStartupValidator.Problems(machine, scope.ServiceProvider);
    }

    public async Task<Trax.Effect.Models.SnapshotDraft.SnapshotDraft?> Row(Guid id, string machine)
    {
        using var scope = Scope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id && x.Machine == machine);
    }

    /// <summary>The work queue entries an instance's invoking states queued, oldest first.</summary>
    public async Task<List<WorkQueue>> Runs(Guid instance)
    {
        using var scope = Scope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .WorkQueues.AsNoTracking()
            .Where(x => x.InvokingInstanceId == instance)
            .OrderBy(x => x.Id)
            .ToListAsync();
    }

    /// <summary>
    /// Dispatches an instance's queued runs the way the dispatcher does: each entry claimed, and its run recorded
    /// in progress under the entry's external id, carrying the entry's link to the instance.
    /// </summary>
    public async Task Dispatch(Guid instance)
    {
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var queued = await db
            .WorkQueues.AsNoTracking()
            .Where(w => w.InvokingInstanceId == instance && w.Status == WorkQueueStatus.Queued)
            .ToListAsync();
        foreach (var entry in queued)
        {
            var run = Metadata.Create(
                new CreateMetadata
                {
                    Name = entry.TrainName,
                    ExternalId = entry.ExternalId,
                    Input = null,
                    InvokedBy = new InvokedBy(
                        entry.InvokingMachine!,
                        instance,
                        entry.InvokingOwnerKind!.Value
                    ),
                }
            );
            run.TrainState = TrainState.InProgress;
            await db.Track(run);
            await db.SaveChanges(CancellationToken.None);
            await db
                .WorkQueues.Where(w => w.Id == entry.Id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(w => w.Status, WorkQueueStatus.Dispatched)
                        .SetProperty(w => w.MetadataId, run.Id)
                );
        }
    }

    /// <summary>Ends every dispatched run of an instance in <paramref name="state"/>.</summary>
    public async Task End(Guid instance, TrainState state)
    {
        using var scope = Scope();
        await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .Metadatas.Where(m => m.InvokingInstanceId == instance)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.TrainState, state));
    }

    /// <summary>Creates <paramref name="user"/>'s draft of <paramref name="machine"/> in <c>Idle</c> and advances it into <c>Running</c>.</summary>
    public async Task<AdvanceOutcome> EnterRunning(string user, Guid id, string machine)
    {
        TestPrincipal.Become(user);
        using var scope = Scope();
        var service = Service(scope, machine);
        var saved = await service.Autosave(
            user,
            id,
            StageMachine<IGoodTrain, GoodInput, JobOutput>.Json(machine, "Idle")
        );
        if (saved is not AutosaveResult.Saved)
            throw new InvalidOperationException($"The draft could not be saved: {saved}");
        return await service.Advance(user, id, "Go");
    }

    public void Dispose()
    {
        _provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (_sqliteFile is null)
            return;
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_sqliteFile);
        }
        catch (IOException)
        {
            // Best effort: a lingering handle can hold the temp file.
        }
    }
}
