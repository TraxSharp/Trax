using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.Fixtures;

/// <summary>The stores the checkpoint tests run on.</summary>
public enum CheckpointStoreKind
{
    Postgres,
    Sqlite,
    InMemory,
}

/// <summary>
/// A host over one of the three data providers for the checkpoint and resume tests: the trains a
/// test registers, a decider it scripts, the log lines it writes, and reads of the rows a run left.
/// </summary>
/// <remarks>
/// Postgres is the shared test database, which <see cref="CheckTraxInvariantsAttribute"/> checks
/// after each test, so each test deletes the runs it made. SQLite is a file of its own and InMemory
/// a store of its own, both dropped with the host.
/// </remarks>
public sealed class CheckpointHost : IAsyncDisposable
{
    private readonly string? _sqliteFile;

    private CheckpointHost(
        ServiceProvider services,
        string? sqliteFile,
        ConcurrentQueue<(LogLevel Level, string Message)> logs
    )
    {
        Services = services;
        _sqliteFile = sqliteFile;
        Logs = logs;
    }

    public ServiceProvider Services { get; }

    /// <summary>Every log line the host wrote, with its level.</summary>
    public ConcurrentQueue<(LogLevel Level, string Message)> Logs { get; }

    public static CheckpointHost Create(
        CheckpointStoreKind store,
        IDecider decider,
        Action<IServiceCollection> configure
    )
    {
        var services = new ServiceCollection();
        var logs = new ConcurrentQueue<(LogLevel Level, string Message)>();
        services.AddLogging(x =>
            x.SetMinimumLevel(LogLevel.Information).AddProvider(new CapturingLoggerProvider(logs))
        );
        services.AddSingleton(decider);

        string? sqliteFile = null;
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                store switch
                {
                    CheckpointStoreKind.Postgres => effects.UsePostgres(PostgresConnection()),
                    CheckpointStoreKind.Sqlite => effects.UseSqlite(
                        $"Data Source={sqliteFile = Path.Combine(Path.GetTempPath(), $"trax_checkpoints_{Guid.NewGuid():N}.db")}"
                    ),
                    _ => effects.UseInMemory(),
                }
            )
        );

        // The step's Trax data context, as a consumer registers it. InMemory registers its own.
        if (store != CheckpointStoreKind.InMemory)
            services.AddScoped<IDataContext>(sp =>
                (IDataContext)sp.GetRequiredService<IDataContextProviderFactory>().Create()
            );

        configure(services);

        return new CheckpointHost(services.BuildServiceProvider(), sqliteFile, logs);
    }

    private static string PostgresConnection() =>
        TestPostgres.WithPort(
            new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build()
                .GetRequiredSection("Configuration")["DatabaseConnectionString"]!
        );

    /// <summary>
    /// Runs <typeparamref name="TTrain"/> on <paramref name="input"/> in a scope of its own, with a
    /// row that resumes <paramref name="resumeFrom"/> at <paramref name="resumeAt"/> when given. A
    /// failure is recorded on the row, which is what a test reads, and is returned too.
    /// </summary>
    public async Task<(Metadata Run, Exception? Failure)> Run<TTrain>(
        string input,
        long? resumeFrom = null,
        string? resumeAt = null
    )
        where TTrain : class, IServiceTrain<string, string>
    {
        using var scope = Services.CreateScope();
        var train =
            (ServiceTrain<string, string>)
                (object)scope.ServiceProvider.GetRequiredService<TTrain>();
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(TTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = input,
                ResumeFrom = resumeFrom,
                ResumeAt = resumeAt,
            }
        );

        Exception? failure = null;
        try
        {
            await train.Run(input, metadata);
        }
        catch (Exception e)
        {
            failure = e;
        }

        return (await Row(train.Metadata!.Id), failure);
    }

    public async Task<Metadata> Row(long id)
    {
        await using var context = await Factory.CreateDbContextAsync(CancellationToken.None);
        return await context.Metadatas.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    public async Task<List<Models.Checkpoint.Checkpoint>> Checkpoints(long runId)
    {
        await using var context = await Factory.CreateDbContextAsync(CancellationToken.None);
        return await context
            .Checkpoints.AsNoTracking()
            .Where(c => c.MetadataId == runId)
            .OrderBy(c => c.NodeId)
            .ToListAsync();
    }

    /// <summary>Changes stored checkpoint rows, as a deploy that changed the code would see them.</summary>
    public async Task Tamper(long runId, Action<Models.Checkpoint.Checkpoint> change)
    {
        await using var context = await Factory.CreateDbContextAsync(CancellationToken.None);
        foreach (
            var row in await context.Checkpoints.Where(c => c.MetadataId == runId).ToListAsync()
        )
            change(row);
        await context.SaveChanges(CancellationToken.None);
    }

    /// <summary>Deletes the runs a test made, their checkpoints going with them.</summary>
    public async Task Delete(params long[] ids)
    {
        await using var context = await Factory.CreateDbContextAsync(CancellationToken.None);
        var rows = await context.Metadatas.Where(m => ids.Contains(m.Id)).ToListAsync();
        var checkpoints = await context
            .Checkpoints.Where(c => ids.Contains(c.MetadataId))
            .ToListAsync();
        context.Checkpoints.RemoveRange(checkpoints);
        context.Metadatas.RemoveRange(rows);
        await context.SaveChanges(CancellationToken.None);
    }

    private IDataContextProviderFactory Factory =>
        Services.GetRequiredService<IDataContextProviderFactory>();

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();

        if (_sqliteFile is null)
            return;

        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            try
            {
                File.Delete(_sqliteFile + suffix);
            }
            catch (IOException)
            {
                // Best effort: a lingering handle can hold the temp file.
            }
    }

    private sealed class CapturingLoggerProvider(
        ConcurrentQueue<(LogLevel Level, string Message)> logs
    ) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Capturing(logs);

        public void Dispose() { }

        private sealed class Capturing(ConcurrentQueue<(LogLevel Level, string Message)> logs)
            : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            ) => logs.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
