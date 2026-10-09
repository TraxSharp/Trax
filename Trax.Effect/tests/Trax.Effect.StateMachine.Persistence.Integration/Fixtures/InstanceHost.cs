using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

/// <summary>The two providers that store drafts in a real database.</summary>
public enum StoreProvider
{
    Postgres,
    Sqlite,
}

/// <summary>
/// A host with <c>AddStateMachines</c> over Postgres (the shared throwaway database) or a fresh SQLite file, built
/// as an application builds one, so the tests reach the registry, the store and <see cref="IMachineInstances"/>
/// through the container.
/// </summary>
public sealed class InstanceHost : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly string? _sqliteFile;

    private InstanceHost(ServiceProvider provider, string? sqliteFile)
    {
        _provider = provider;
        _sqliteFile = sqliteFile;
    }

    internal static InstanceHost Create(
        StoreProvider provider,
        TimeSpan? draftTtl = null,
        IInvokedRunCancellation? runCancellation = null,
        IInvokedTrainLauncher? launcher = null
    )
    {
        var file =
            provider == StoreProvider.Sqlite
                ? Path.Combine(Path.GetTempPath(), $"sm_instances_{Guid.NewGuid():N}.db")
                : null;

        var services = new ServiceCollection();
        services.AddScoped<IOrderCharge, CountingEffect>();
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                    file is null
                        ? effects.UsePostgres(PostgresSetup.ConnectionString)
                        : effects.UseSqlite($"Data Source={file}")
                )
                .AddStateMachines(o => o.DraftTtl = draftTtl, typeof(OrderMachine).Assembly)
        );
        if (runCancellation is not null)
            services.AddSingleton(runCancellation);
        if (launcher is not null)
            services.AddSingleton(launcher);

        return new InstanceHost(services.BuildServiceProvider(), file);
    }

    public IServiceScope Scope() => _provider.CreateScope();

    public async Task<MachineInstance> Start<TMachine>(
        MachineKey key,
        System.Text.Json.Nodes.JsonObject? context = null
    )
        where TMachine : IMachine
    {
        using var scope = Scope();
        return await scope
            .ServiceProvider.GetRequiredService<IMachineInstances>()
            .Start<TMachine>(key, context);
    }

    public ISnapshotDraftService Service(IServiceScope scope, string machine) =>
        scope.ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>().Service(machine)!;

    internal IMachineInstanceStore Instances(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IMachineInstanceStore>();

    /// <summary>Every row under <paramref name="id"/>, whoever owns it, read straight from the table.</summary>
    public async Task<List<Trax.Effect.Models.SnapshotDraft.SnapshotDraft>> Rows(Guid id)
    {
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return await db.SnapshotDrafts.AsNoTracking().Where(x => x.Id == id).ToListAsync();
    }

    /// <summary>Every work queue entry with <paramref name="externalId"/>, read straight from the table.</summary>
    public async Task<List<Trax.Effect.Models.WorkQueue.WorkQueue>> Entries(string externalId)
    {
        using var scope = Scope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .WorkQueues.AsNoTracking()
            .Where(x => x.ExternalId == externalId)
            .ToListAsync();
    }

    public async Task<int> WorkQueueCount()
    {
        using var scope = Scope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .WorkQueues.CountAsync();
    }

    /// <summary>Makes a row look idle since <paramref name="updatedAt"/> (the store always stamps "now").</summary>
    public async Task Backdate(Guid id, SnapshotOwnerKind kind, DateTimeOffset updatedAt)
    {
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<IDataContext>();
        await db
            .SnapshotDrafts.Where(x => x.Id == id && x.OwnerKind == kind)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, updatedAt));
    }

    public void Dispose()
    {
        _provider.Dispose();
        if (_sqliteFile is null)
            return;
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_sqliteFile);
        }
        catch (IOException)
        {
            // Best effort: a lingering handle can hold the temp file; the OS reaps it later.
        }
    }
}
