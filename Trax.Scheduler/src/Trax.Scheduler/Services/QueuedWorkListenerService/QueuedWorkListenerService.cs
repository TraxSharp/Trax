using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.QueuedWorkListener;
using Trax.Scheduler.Services.JobDispatcherPollingService;

namespace Trax.Scheduler.Services.QueuedWorkListenerService;

/// <summary>
/// Wakes the job dispatcher when the data provider reports that work was queued, so a run queued
/// on another host starts without waiting for the dispatcher's next poll.
/// </summary>
/// <remarks>
/// <para>The notice comes from the provider's <see cref="IQueuedWorkListener"/>: on Postgres a
/// <c>NOTIFY</c> sent when a transaction that queued work commits, heard from every host; on SQLite
/// an in-process notice. A provider that registers none leaves this service with nothing to do,
/// and the dispatcher polls as it always has.</para>
///
/// <para>The poll stays the fallback. When the subscription is lost (the connection is closed or
/// the database restarts) this logs it, waits a backoff that grows to <see cref="MaxBackoff"/>, and
/// subscribes again, while the dispatcher carries on polling. A fresh subscription signals one wake
/// itself, because anything committed while it was down was not heard. Nothing here stops the
/// host.</para>
/// </remarks>
internal sealed class QueuedWorkListenerService(
    IServiceProvider serviceProvider,
    DispatcherWake wake,
    ILogger<QueuedWorkListenerService> logger
) : BackgroundService
{
    internal static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private int _subscriptions;
    private int _notices;

    /// <summary>How many times a subscription has been opened, the first one included.</summary>
    internal int Subscriptions => Volatile.Read(ref _subscriptions);

    /// <summary>How many notices have arrived across every subscription.</summary>
    internal int Notices => Volatile.Read(ref _notices);

    /// <summary>Raised after each subscription opens.</summary>
    internal event Action? Subscribed;

    /// <summary>Raised after each notice, once the dispatcher has been signalled.</summary>
    internal event Action? NoticeReceived;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var listener = serviceProvider.GetService<IQueuedWorkListener>();
        if (listener is null)
        {
            logger.LogDebug(
                "The data provider sends no notice of queued work; the job dispatcher polls only"
            );
            return;
        }

        var backoff = FirstBackoff;
        while (!stoppingToken.IsCancellationRequested)
        {
            IQueuedWorkSubscription? subscription = null;
            try
            {
                subscription = await listener.SubscribeAsync(stoppingToken);

                if (Interlocked.Increment(ref _subscriptions) > 1)
                {
                    logger.LogInformation("Listening for queued work again");
                    wake.Signal();
                }
                backoff = FirstBackoff;
                Subscribed?.Invoke();

                while (true)
                {
                    await subscription.WaitAsync(stoppingToken);
                    Interlocked.Increment(ref _notices);
                    wake.Signal();
                    NoticeReceived?.Invoke();
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Lost the notice of queued work; the job dispatcher polls until it is back. "
                        + "Listening again in {Backoff}",
                    backoff
                );
            }
            finally
            {
                if (subscription is not null)
                    await DisposeQuietly(subscription);
            }

            try
            {
                await Task.Delay(backoff, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = backoff * 2 < MaxBackoff ? backoff * 2 : MaxBackoff;
        }
    }

    private async Task DisposeQuietly(IQueuedWorkSubscription subscription)
    {
        try
        {
            await subscription.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Closing a lost queued-work subscription failed");
        }
    }
}
