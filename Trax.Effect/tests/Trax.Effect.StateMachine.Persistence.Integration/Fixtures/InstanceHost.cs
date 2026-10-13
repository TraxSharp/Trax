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
        IInvokedTrainLauncher? launcher = null,
        Action<StateMachineOptions>? options = null,
        Action<IServiceCollection>? services = null,
        string? postgresConnectionString = null
    )
    {
        var file =
            provider == StoreProvider.Sqlite
                ? Path.Combine(Path.GetTempPath(), $"sm_instances_{Guid.NewGuid():N}.db")
                : null;

        var collection = new ServiceCollection();
        // The outcome reconciler and delivery log, as a host's container provides.
        collection.AddLogging();
        collection.AddScoped<IOrderCharge, CountingEffect>();
        collection.AddTrax(trax =>
            trax.AddEffects(effects =>
                    file is null
                        ? effects.UsePostgres(
                            postgresConnectionString ?? PostgresSetup.ConnectionString
                        )
                        : effects.UseSqlite($"Data Source={file}")
                )
                .AddStateMachines(
                    o =>
                    {
                        o.DraftTtl = draftTtl;
                        options?.Invoke(o);
                    },
                    typeof(OrderMachine).Assembly
                )
        );
        if (runCancellation is not null)
            collection.AddSingleton(runCancellation);
        if (launcher is not null)
            collection.AddSingleton(launcher);
        services?.Invoke(collection);

        return new InstanceHost(collection.BuildServiceProvider(), file);
    }

    public IServiceScope Scope() => _provider.CreateScope();

    /// <summary>The host's container, for the singletons a test drives directly (the reconciler, the listener).</summary>
    public IServiceProvider Services => _provider;

    /// <summary>Which provider the host stores drafts in.</summary>
    public StoreProvider Provider =>
        _sqliteFile is null ? StoreProvider.Postgres : StoreProvider.Sqlite;

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

    /// <summary>
    /// Creates <paramref name="user"/>'s draft from <paramref name="json"/> as host code does, through the public
    /// <see cref="ISnapshotStore"/>: a machine with a rule, a timer, an invoke or an effect moves through its declared
    /// events only, so no save creates its draft.
    /// </summary>
    public async Task SeedDraft(string user, Guid id, string json)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        var snapshot = new Snapshot
        {
            Machine = node["machine"]!.GetValue<string>(),
            Version = node["version"]!.GetValue<int>(),
            State = node["state"]!.GetValue<string>(),
            Context = (System.Text.Json.Nodes.JsonObject)node["context"]!.DeepClone(),
        };
        using var scope = Scope();
        if (
            !await scope
                .ServiceProvider.GetRequiredService<ISnapshotStore>()
                .Upsert(user, id, snapshot)
        )
            throw new InvalidOperationException($"The draft {id} could not be seeded.");
    }

    internal IMachineInstanceStore Instances(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IMachineInstanceStore>();

    /// <summary>Every row under <paramref name="id"/>, whoever owns it, read straight from the table.</summary>
    public async Task<List<Trax.Effect.Models.SnapshotDraft.SnapshotDraft>> Rows(Guid id)
    {
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return await db.SnapshotDrafts.AsNoTracking().Where(x => x.Id == id).ToListAsync();
    }

    /// <summary>Every row of <paramref name="machine"/> that <paramref name="userKey"/> owns, read straight from the table.</summary>
    public async Task<List<Trax.Effect.Models.SnapshotDraft.SnapshotDraft>> UserRows(
        string userKey,
        string machine
    )
    {
        using var scope = Scope();
        var db = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return await db
            .SnapshotDrafts.AsNoTracking()
            .Where(x =>
                x.OwnerKind == SnapshotOwnerKind.User
                && x.UserKey == userKey
                && x.Machine == machine
            )
            .ToListAsync();
    }

    /// <summary>Writes <paramref name="snapshot"/> as <paramref name="userKey"/>'s draft straight through the store.</summary>
    public async Task Seed(string userKey, Guid id, Snapshot snapshot)
    {
        using var scope = Scope();
        if (
            !await scope
                .ServiceProvider.GetRequiredService<ISnapshotStore>()
                .Upsert(userKey, id, snapshot)
        )
            throw new InvalidOperationException($"Could not seed draft {id} for {userKey}.");
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

    /// <summary>The draft table as raw SQL names it on this host's provider.</summary>
    public string DraftTable => _sqliteFile is null ? "trax.snapshot_draft" : "snapshot_draft";

    /// <summary>
    /// Runs <paramref name="sql"/> on this host's database, as host code outside Trax would, with
    /// <paramref name="parameters"/> bound as <c>@p0</c>, <c>@p1</c> and so on; returns the rows it changed.
    /// </summary>
    public async Task<int> Execute(string sql, params object?[] parameters)
    {
        using var scope = Scope();
        await using var command = await Command(scope, sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>The first column of the first row <paramref name="sql"/> reads, as <see cref="Execute"/> binds it.</summary>
    public async Task<object?> Scalar(string sql, params object?[] parameters)
    {
        using var scope = Scope();
        await using var command = await Command(scope, sql, parameters);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    /// <summary>The surrogate key of the row <paramref name="kind"/> holds under <paramref name="id"/>.</summary>
    public async Task<long> RowId(Guid id, SnapshotOwnerKind kind = SnapshotOwnerKind.User) =>
        (await Rows(id)).Single(x => x.OwnerKind == kind).RowId;

    /// <summary>The <c>revision</c> column of the row <paramref name="kind"/> holds under <paramref name="id"/>, read with raw SQL.</summary>
    public async Task<long> Revision(Guid id, SnapshotOwnerKind kind = SnapshotOwnerKind.User) =>
        Convert.ToInt64(
            await Scalar(
                $"SELECT revision FROM {DraftTable} WHERE row_id = @p0",
                await RowId(id, kind)
            ),
            System.Globalization.CultureInfo.InvariantCulture
        );

    private static async Task<System.Data.Common.DbCommand> Command(
        IServiceScope scope,
        string sql,
        object?[] parameters
    )
    {
        var connection = (
            (DbContext)scope.ServiceProvider.GetRequiredService<IDataContext>()
        ).Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@p{i}";
            parameter.Value = parameters[i] ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
        return command;
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
        // Async: a test that resolves the outcome reconciler builds the Postgres listener, which only disposes async.
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
            // Best effort: a lingering handle can hold the temp file; the OS reaps it later.
        }
    }
}
