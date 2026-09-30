using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Effect.Data.Services.DataContextLoggingProvider;

/// <summary>
/// An <see cref="ILoggerProvider"/> that stores application log entries in the <c>trax.log</c> table
/// through <see cref="IDataContext.Logs"/>. Registered by <c>AddDataContextLogging</c>; not intended to
/// be constructed directly.
/// </summary>
/// <remarks>
/// Loggers queue entries into a bounded in-memory queue of 4096; when it is full the oldest entry is
/// dropped. A single background loop, started by the constructor, writes them in batches of up to 256
/// as they arrive, using one long-lived data context. <see cref="Dispose"/> lets it write what is
/// already queued before it stops. If a batch fails, its entries are retried
/// one at a time and any that still fail are dropped, so logging never throws into the caller.
/// </remarks>
public class DataContextLoggingProvider : IDataContextLoggingProvider
{
    private readonly IDataContextProviderFactory _dbContextFactory;
    private readonly IDataContextLoggingProviderConfiguration _configuration;
    private readonly HashSet<string> _exactBlacklist = [];
    private readonly List<Regex> _wildcardBlacklist = [];

    private readonly Channel<Effect.Models.Log.Log> _logChannel =
        Channel.CreateBounded<Effect.Models.Log.Log>(
            new BoundedChannelOptions(4096)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            }
        );

    private readonly CancellationTokenSource _cts = new();
    private readonly Task _flushTask;

    /// <summary>
    /// Compiles the blacklist and starts the background writer. A blacklist entry containing
    /// <c>*</c> is a wildcard (<c>*</c> matches any run of characters, anchored at both ends); any
    /// other entry must match the category exactly.
    /// </summary>
    /// <param name="dbContextFactory">Creates the one data context the writer uses for its lifetime.</param>
    /// <param name="configuration">Minimum level and blacklist, read once here.</param>
    public DataContextLoggingProvider(
        IDataContextProviderFactory dbContextFactory,
        IDataContextLoggingProviderConfiguration configuration
    )
    {
        _dbContextFactory = dbContextFactory;
        _configuration = configuration;

        foreach (var pattern in configuration.Blacklist)
        {
            if (pattern.Contains('*'))
            {
                // Convert wildcard to regex, escaping dots and replacing '*' with '.*'
                var regexPattern = "^" + Regex.Escape(pattern).Replace(@"\*", ".*") + "$";
                _wildcardBlacklist.Add(new Regex(regexPattern, RegexOptions.Compiled));
            }
            else
                _exactBlacklist.Add(pattern);
        }

        _flushTask = Task.Run(() => FlushLoopAsync(_cts.Token));
    }

    /// <summary>
    /// Returns a new <see cref="DataContextLogger"/> for <paramref name="categoryName"/> that writes
    /// into this provider's queue.
    /// </summary>
    /// <param name="categoryName">The category stored on each entry and matched against the blacklist.</param>
    public ILogger CreateLogger(string categoryName)
    {
        return new DataContextLogger(
            _logChannel.Writer,
            categoryName,
            _configuration.MinimumLogLevel,
            _exactBlacklist,
            _wildcardBlacklist
        );
    }

    private async Task FlushLoopAsync(CancellationToken ct)
    {
        const int maxBatchSize = 256;
        var batch = new List<Effect.Models.Log.Log>(maxBatchSize);
        var reader = _logChannel.Reader;

        try
        {
            using var dataContext = await _dbContextFactory.CreateDbContextAsync(ct);

            // WaitToReadAsync returns false only once the queue is completed and empty, so after
            // Dispose completes it the loop still writes everything queued before that, then ends.
            while (await reader.WaitToReadAsync(ct))
            {
                batch.Clear();
                // The loop is the queue's only reader, so after WaitToReadAsync returns true the
                // batch holds at least one entry.
                while (batch.Count < maxBatchSize && reader.TryRead(out var log))
                    batch.Add(log);

                try
                {
                    await dataContext.Logs.AddRangeAsync(batch, ct);
                    await dataContext.SaveChanges(ct);
                    dataContext.Reset();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // One entry the database refuses fails the whole batch, so store the entries
                    // one at a time and lose only the ones that still fail.
                    dataContext.Reset();
                    if (!await SaveOneByOne(dataContext, batch, ct))
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Dispose gave up waiting for the queue to drain.
        }
    }

    /// <summary>
    /// Stores each entry of a failed batch on its own. An entry that still fails is dropped
    /// rather than retried, so a bad entry cannot hold up the loop. Returns <c>false</c> when
    /// shutdown cancelled it.
    /// </summary>
    private static async Task<bool> SaveOneByOne(
        IDataContext dataContext,
        List<Effect.Models.Log.Log> batch,
        CancellationToken ct
    )
    {
        foreach (var log in batch)
        {
            try
            {
                await dataContext.Logs.AddAsync(log, ct);
                await dataContext.SaveChanges(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return false;
            }
            catch
            {
                // Logging failures should not crash the application.
            }
            finally
            {
                dataContext.Reset();
            }
        }
        return true;
    }

    /// <summary>
    /// Stops accepting entries and waits up to five seconds for the background writer to store the
    /// ones already queued. Only if that runs out is the writer cancelled, and whatever it has not
    /// written by then is lost. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _logChannel.Writer.TryComplete();

        if (!WaitForWriter(DrainTimeout))
        {
            _cts.Cancel();
            WaitForWriter(TimeSpan.FromSeconds(1));
        }

        _cts.Dispose();
    }

    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    private int _disposed;

    /// <summary>
    /// True when the writer has stopped within <paramref name="timeout"/>. A writer that failed
    /// (its database unreachable, say) has stopped too, and logging never throws into the caller.
    /// </summary>
    private bool WaitForWriter(TimeSpan timeout)
    {
        try
        {
            return _flushTask.Wait(timeout);
        }
        catch (AggregateException)
        {
            return true;
        }
    }
}
