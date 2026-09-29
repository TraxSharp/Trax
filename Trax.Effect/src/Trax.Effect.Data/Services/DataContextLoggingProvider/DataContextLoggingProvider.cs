using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Effect.Data.Services.DataContextLoggingProvider;

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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        using var dataContext = await _dbContextFactory.CreateDbContextAsync(ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Wait for either new data or the 1-second timer tick
                var dataAvailable = _logChannel.Reader.TryRead(out var firstLog);

                if (!dataAvailable)
                {
                    // Wait for whichever comes first: data or timer
                    using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var timerTask = timer.WaitForNextTickAsync(delayCts.Token).AsTask();
                    var readTask = _logChannel.Reader.WaitToReadAsync(delayCts.Token).AsTask();

                    await Task.WhenAny(timerTask, readTask);
                    await delayCts.CancelAsync();

                    dataAvailable = _logChannel.Reader.TryRead(out firstLog);
                    if (!dataAvailable)
                        continue;
                }

                batch.Clear();
                batch.Add(firstLog!);

                while (batch.Count < maxBatchSize && _logChannel.Reader.TryRead(out var log))
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
            // Normal shutdown
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

    public void Dispose()
    {
        _logChannel.Writer.TryComplete();
        _cts.Cancel();

        // Best-effort wait for the flush loop to drain
        _flushTask.Wait(TimeSpan.FromSeconds(5));

        _cts.Dispose();
    }
}
